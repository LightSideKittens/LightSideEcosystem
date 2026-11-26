#!/usr/bin/env python3
"""
Renders the system-font paragraph of a slideshow job card from the SystemFontCuts cases of the (merged) JUnit results:
one row per device with the violations of every check, and one row per device and script with what resolving costs.
Usage: python system-font-summary.py <testResults.xml> <platform>
"""

import sys
import xml.etree.ElementTree as ET

CASE_CLASS = 'SystemFontCuts'
MEASURED = ('Latin', 'Han', 'Thai', 'Arabic')


def cell(text: str) -> str:
    return text.replace('|', '\\|')


def main():
    if len(sys.argv) != 3:
        print(f"Usage: {sys.argv[0]} <testResults.xml> <platform>")
        sys.exit(1)
    sys.stdout.reconfigure(encoding='utf-8')

    devices = {}
    for case in ET.parse(sys.argv[1]).getroot().iter('testcase'):
        classname = case.get('classname', '')
        if classname != CASE_CLASS and not classname.endswith('.' + CASE_CLASS):
            continue
        failure = case.find('failure')
        devices.setdefault(classname, {})[case.get('name')] = (
            failure.get('message') if failure is not None else None, case.findtext('system-out') or '')

    print()
    print('#### System-font cuts')
    if not devices:
        print('❌ No system-font checks in the results.')
        return

    checks = list(dict.fromkeys(name for cases in devices.values() for name in cases))
    print()
    print('| Device | System font | ' + ' | '.join(cell(c) for c in checks) + ' |')
    print('| --- | --- |' + ' --- |' * len(checks))
    times = []
    for cases in devices.values():
        facts = dict(line.split('\t', 1) for line in cases.get('environment', (None, ''))[1].splitlines() if '\t' in line)
        device = facts.get('device', '?')
        marks = []
        for check in checks:
            if check not in cases:
                marks.append('—')
            else:
                message = cases[check][0]
                marks.append('✅' if message is None else '❌ ' + message.split(None, 1)[0])
        print(f"| {cell(device)} | {cell(facts.get('default', '?'))} | " + ' | '.join(marks) + ' |')

        for line in cases.get('performance', (None, ''))[1].splitlines()[1:]:
            fields = line.split('\t')
            if len(fields) == 9 and fields[0] in MEASURED:
                times.append((device, fields))

    print()
    print('| Device | Script | Weight | First cluster | Next cluster | Cached | Text, cold | Text, warm |')
    print('| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |')
    for device, f in times:
        print(f"| {cell(device)} | {f[0]} | {f[1]} | {float(f[4]):.2f} ms | {float(f[5]) * 1000:.1f} µs | "
              f"{float(f[6]):.2f} µs | {float(f[7]):.1f} ms | {float(f[8]):.1f} ms |")
    print()
    print('A check counts weights that lose a cluster another weight resolves, cuts farther from the request than one the '
          'platform gives for another weight, faces whose weight or slant contradicts their style, and answers a text, a '
          'thread or a reset changes.')


if __name__ == '__main__':
    main()
