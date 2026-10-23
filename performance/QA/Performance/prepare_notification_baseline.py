"""Prepare one authenticated Notification List request; sends no traffic."""
from pathlib import Path
import xml.etree.ElementTree as E

root = Path(__file__).resolve().parents[2]
tree = E.parse(root / 'List Orders Direct Azure.jmx')
doc = tree.getroot()

def set_prop(parent, name, value, kind='stringProp'):
    node = parent.find(f"*[@name='{name}']")
    if node is None:
        node = E.SubElement(parent, kind, {'name': name})
    node.text = value

group = doc.find('.//ThreadGroup')
set_prop(group, 'ThreadGroup.num_threads', '1')
set_prop(group, 'ThreadGroup.ramp_time', '1')
set_prop(group, 'ThreadGroup.scheduler', 'false', 'boolProp')
loop = group.find("elementProp[@name='ThreadGroup.main_controller']")
set_prop(loop, 'LoopController.loops', '1')
set_prop(loop, 'LoopController.continue_forever', 'false', 'boolProp')
sampler = doc.find('.//HTTPSamplerProxy')
assert len(list(doc.iter('HTTPSamplerProxy'))) == 1
sampler.set('testname', 'GET - List Notifications')
set_prop(sampler, 'HTTPSampler.domain', 'sellora-notification-staging-g6csbfcqc6gmhtgd.eastasia-01.azurewebsites.net')
set_prop(sampler, 'HTTPSampler.protocol', 'https')
set_prop(sampler, 'HTTPSampler.port', '443')
set_prop(sampler, 'HTTPSampler.path', '/api/notifications')
set_prop(sampler, 'HTTPSampler.connect_timeout', '5000')
set_prop(sampler, 'HTTPSampler.response_timeout', '10000')
for argument in sampler.findall(".//elementProp[@elementType='HTTPArgument']"):
    name = argument.find("stringProp[@name='Argument.name']")
    if name is not None and name.text == 'pageSize':
        set_prop(argument, 'Argument.value', '20')
for header in doc.findall(".//elementProp[@elementType='Header']"):
    if header.find("stringProp[@name='Header.name']").text == 'Authorization':
        set_prop(header, 'Header.value', "Bearer ${__groovy(System.getenv('SELLORA_ADMIN_ACCESS_TOKEN'))}")
for listener in doc.iter('ResultCollector'):
    listener.set('enabled', 'false')
for timer in doc.iter('ConstantTimer'):
    set_prop(timer, 'ConstantTimer.delay', '0')
for assertion in doc.iter('ResponseAssertion'):
    assert assertion.find("stringProp[@name='Assertion.test_field']").text == 'Assertion.response_code'
    values = assertion.find("collectionProp[@name='Asserion.test_strings']")
    assert len(values) == 1 and values[0].text == '200'
for assertion in doc.iter('JSONPathAssertion'):
    assert assertion.find("stringProp[@name='JSON_PATH']").text == '$.items'
E.indent(tree, space='  ')
tree.write(root / 'Notification List Baseline.jmx', encoding='UTF-8', xml_declaration=True)
print('Prepared exactly one GET /api/notifications with status and JSON checks.')
print('Authorization reads SELLORA_ADMIN_ACCESS_TOKEN.')
