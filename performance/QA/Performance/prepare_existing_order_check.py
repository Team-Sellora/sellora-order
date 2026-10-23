from pathlib import Path
import xml.etree.ElementTree as E
import copy

root = Path(__file__).resolve().parents[2]
tree = E.parse(root / 'Create and Read Order.jmx')
doc = tree.getroot()
thread = doc.find('.//ThreadGroup')
containers = doc.findall('.//hashTree')
group_tree = next(h[list(h).index(thread) + 1] for h in containers if thread in list(h))
nodes = list(group_tree)
header = next(x for x in nodes if x.tag == 'HeaderManager')
header_tree = nodes[nodes.index(header) + 1]
get = next(x for x in doc.iter('HTTPSamplerProxy') if x.get('testname') == 'GET - Read Created Order')
parent = next(h for h in containers if get in list(h))
get_tree = copy.deepcopy(list(parent)[list(parent).index(get) + 1])
get = copy.deepcopy(get)
order_id = '6302f5bc-3ced-4b6e-b830-e0ef7db1128b'
get.find("stringProp[@name='HTTPSampler.path']").text = '/api/orders/' + order_id
get_tree.find("JSONPathAssertion/stringProp[@name='EXPECTED_VALUE']").text = order_id
assert get_tree.find("ResponseAssertion/stringProp[@name='Assertion.test_field']").text == 'Assertion.response_code', 'Save the corrected GET assertion first'
thread.find("*[@name='ThreadGroup.num_threads']").text = '1'
thread.find("*[@name='ThreadGroup.ramp_time']").text = '1'
schedule = thread.find("boolProp[@name='ThreadGroup.scheduler']")
if schedule is None:
    schedule = E.SubElement(thread, 'boolProp', {'name': 'ThreadGroup.scheduler'})
schedule.text = 'false'
loop = thread.find("elementProp[@name='ThreadGroup.main_controller']")
loop.find("*[@name='LoopController.loops']").text = '1'
loop.find("boolProp[@name='LoopController.continue_forever']").text = 'false'
group_tree[:] = [copy.deepcopy(header), copy.deepcopy(header_tree), get, get_tree]
assert len(list(doc.iter('HTTPSamplerProxy'))) == 1
E.indent(tree, space='  ')
tree.write(root / 'Verify Existing Order.jmx', encoding='UTF-8', xml_declaration=True)
print('Created verification plan with exactly one GET and no POST requests.')
