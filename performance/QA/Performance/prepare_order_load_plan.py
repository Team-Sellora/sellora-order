"""Create a bounded write-test plan from the verified baseline; sends no traffic."""
from pathlib import Path
import copy
import json
import xml.etree.ElementTree as E

root = Path(__file__).resolve().parents[2]
tree = E.parse(root / 'Create and Read Order.jmx')
doc = tree.getroot()
group = doc.find('.//ThreadGroup')

def set_property(parent, name, value, kind='stringProp'):
    node = parent.find(f"*[@name='{name}']")
    if node is None:
        node = E.SubElement(parent, kind, {'name': name})
    node.text = value

set_property(group, 'ThreadGroup.num_threads', '5')
set_property(group, 'ThreadGroup.ramp_time', '5')
set_property(group, 'ThreadGroup.scheduler', 'false', 'boolProp')
loop = group.find("elementProp[@name='ThreadGroup.main_controller']")
set_property(loop, 'LoopController.loops', '2')
set_property(loop, 'LoopController.continue_forever', 'false', 'boolProp')

samplers = list(doc.iter('HTTPSamplerProxy'))
assert len(samplers) == 2
post = next(s for s in samplers if s.find("*[@name='HTTPSampler.method']").text == 'POST')
get = next(s for s in samplers if s.find("*[@name='HTTPSampler.method']").text == 'GET')
assert post.find("*[@name='HTTPSampler.postBodyRaw']").text == 'true'
assert get.find("*[@name='HTTPSampler.path']").text == '/api/orders/${createdOrderId}'
body = post.find(".//stringProp[@name='Argument.value']").text
payload = json.loads(body)
assert payload['shopId'] == '63cb5732-c747-454c-8236-f2579ba754ee'
assert payload['fulfilmentType'] == 'ScheduledDelivery'
assert len(payload['lines']) == 1 and payload['lines'][0]['quantity'] == 1
for s in samplers:
    set_property(s, 'HTTPSampler.connect_timeout', '5000')
    set_property(s, 'HTTPSampler.response_timeout', '10000')

containers = doc.findall('.//hashTree')
parent = next(h for h in containers if post in list(h))
post_tree = list(parent)[list(parent).index(post) + 1]
extractor = post_tree.find('JSONPostProcessor')
set_property(extractor, 'JSONPostProcessor.referenceNames', 'createdOrderId;createdOrderReference;createdReservationId;createdOrderTotal')
set_property(extractor, 'JSONPostProcessor.jsonPathExprs', '$.orderId;$.orderReference;$.reservationId;$.total')
set_property(extractor, 'JSONPostProcessor.match_numbers', '1;1;1;1')
set_property(extractor, 'JSONPostProcessor.defaultValues', 'NOT_FOUND;NOT_FOUND;NOT_FOUND;NOT_FOUND')

reset = E.SubElement(post_tree, 'JSR223PreProcessor', {
    'guiclass': 'TestBeanGUI', 'testclass': 'JSR223PreProcessor',
    'testname': 'Reset order correlation before each creation', 'enabled': 'true'})
set_property(reset, 'scriptLanguage', 'groovy')
set_property(reset, 'cacheKey', 'true')
set_property(reset, 'parameters', '')
set_property(reset, 'filename', '')
set_property(reset, 'script', "['createdOrderId', 'createdOrderReference', 'createdReservationId', 'createdOrderTotal'].each { vars.put(it, 'NOT_FOUND') }")
E.SubElement(post_tree, 'hashTree')

# Preserve the existing HTTP status and JSON-ID assertions. No retry or cleanup
# requests are added; a failed creation is reconciled before any manual retry.
get_parent = next(h for h in containers if get in list(h))
get_tree = list(get_parent)[list(get_parent).index(get) + 1]
assert get_tree.find("ResponseAssertion/stringProp[@name='Assertion.test_field']").text == 'Assertion.response_code'
assert get_tree.find("JSONPathAssertion/stringProp[@name='EXPECTED_VALUE']").text == '${createdOrderId}'

for listener in doc.iter('ResultCollector'):
    listener.set('enabled', 'false')
E.indent(tree, space='  ')
output = root / 'Create and Read Order Load.jmx'
tree.write(output, encoding='UTF-8', xml_declaration=True)
print('Prepared 5 clients x 2 iterations: at most 10 POSTs and 10 conditional GETs.')
print('Order ID, reference, reservation ID and total can be saved using sample_variables.')
