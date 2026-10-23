"""Prepare the final bounded Notification List load run; sends no traffic."""
from pathlib import Path
import xml.etree.ElementTree as E

root = Path(__file__).resolve().parents[2]
tree = E.parse(root / 'Notification List Baseline.jmx')
doc = tree.getroot()

def set_prop(parent, name, value, kind='stringProp'):
    node = parent.find(f"*[@name='{name}']")
    if node is None:
        node = E.SubElement(parent, kind, {'name': name})
    node.text = value

group = doc.find('.//ThreadGroup')
set_prop(group, 'ThreadGroup.num_threads', '5')
set_prop(group, 'ThreadGroup.ramp_time', '5')
set_prop(group, 'ThreadGroup.scheduler', 'true', 'boolProp')
set_prop(group, 'ThreadGroup.duration', '60')
set_prop(group, 'ThreadGroup.delay', '0')
loop = group.find("elementProp[@name='ThreadGroup.main_controller']")
set_prop(loop, 'LoopController.loops', '-1')
set_prop(loop, 'LoopController.continue_forever', 'false', 'boolProp')
timers = list(doc.iter('ConstantTimer'))
assert len(timers) == 1
set_prop(timers[0], 'ConstantTimer.delay', '1000')
samplers = list(doc.iter('HTTPSamplerProxy'))
assert len(samplers) == 1
assert samplers[0].find("*[@name='HTTPSampler.method']").text == 'GET'
assert samplers[0].find("*[@name='HTTPSampler.path']").text == '/api/notifications'
assert 'SELLORA_ADMIN_ACCESS_TOKEN' in E.tostring(doc, encoding='unicode')
E.indent(tree, space='  ')
tree.write(root / 'Notification List Load.jmx', encoding='UTF-8', xml_declaration=True)
print('Prepared Notification List Load.jmx: 5 clients, 5-second ramp, 60-second duration, 1000 ms pause, GET only.')
