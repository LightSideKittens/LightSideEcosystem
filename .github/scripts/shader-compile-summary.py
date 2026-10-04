#!/usr/bin/env python3
"""
Renders the shader compilation paragraph of a slideshow job card: one row per device, one column per
shader, from the ShaderCompile cases of the (merged) JUnit results.
Usage: python shader-compile-summary.py <testResults.xml> <platform>
"""

import json
import sys
import xml.etree.ElementTree as ET

CASE_CLASS = 'ShaderCompile'


def format_ms(value: float) -> str:
    return f"{value:.1f} ms" if value < 10 else f"{value:.0f} ms"


def cell(text: str) -> str:
    return text.replace('|', '\\|')


def main():
    if len(sys.argv) != 3:
        print(f"Usage: {sys.argv[0]} <testResults.xml> <platform>")
        sys.exit(1)

    results_path, platform = sys.argv[1], sys.argv[2]
    sys.stdout.reconfigure(encoding='utf-8')

    print()
    print('#### Shader compilation on first display')
    if platform == 'WebGPU':
        print('Not measured: WebGPU has no synchronous GPU readback.')
        return

    rows = {}
    shaders = []
    profiles = []
    errors = []
    for case in ET.parse(results_path).getroot().iter('testcase'):
        classname = case.get('classname', '')
        if classname != CASE_CLASS and not classname.endswith('.' + CASE_CLASS):
            continue

        record = json.loads(case.findtext('system-out'))
        shader = case.get('name')
        if shader not in shaders:
            shaders.append(shader)
        if record['features'] not in profiles:
            profiles.append(record['features'])

        row = rows.setdefault(classname, {
            'device': record['device'],
            'gpu': f"{record['gpu']} · {record['api']}",
            'cells': {},
        })
        failure = case.find('failure')
        if failure is None:
            row['cells'][shader] = format_ms(max(0.0, record['firstMs'] - record['warmMs']))
        else:
            row['cells'][shader] = '❌'
            message = (failure.get('message') or '').splitlines()
            errors.append(f"{record['device']} · {shader}: {message[0] if message else 'no message'}")

    if not rows:
        print('❌ No shader compilation measurements in the results.')
        return

    print()
    print('| Device | GPU · API | ' + ' | '.join(cell(s) for s in shaders) + ' |')
    print('| --- | --- |' + ' ---: |' * len(shaders))
    for row in rows.values():
        cells = [row['cells'].get(s, '—') for s in shaders]
        print(f"| {cell(row['device'])} | {cell(row['gpu'])} | " + ' | '.join(cells) + ' |')
    print()
    for error in errors:
        print(f"- ❌ {error}")
    if errors:
        print()

    print("Compile = a shader's first draw minus its fastest warm draw, measured at launch before the "
          f"first frame, one render state per shader. LightSide profile: {'; '.join(profiles)}.")
    if platform == 'iOS':
        print()
        print('iOS keeps compiled Metal shaders in a system cache until the device restarts, even across '
              'reinstalls: a time near zero means the Test Lab device compiled these shaders in an earlier run.')


if __name__ == '__main__':
    main()
