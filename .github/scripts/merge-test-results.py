#!/usr/bin/env python3
"""
Merges testResults.xml files from multiple devices into a single JUnit XML.
Usage: python merge-test-results.py <output.xml> <input1.xml> [input2.xml ...]
"""

import sys
import re
import xml.etree.ElementTree as ET
from pathlib import Path


def extract_device_name(filepath: str) -> str:
    """Extract device name from Firebase results path."""
    match = re.search(
        r'(?:^|[/\\])(?:bundle-\d+-)?([^/\\]+)-[\d.]+-[A-Za-z_]+-(?:portrait|landscape)(?=[/\\]|$)',
        filepath,
    )
    if not match:
        raise ValueError(f"Firebase device axis not found in path: {filepath}")
    return match.group(1)


def merge_results(output_path: str, input_paths: list[str]) -> tuple[int, int]:
    """Merge multiple testResults.xml files into one."""
    total_tests = 0
    total_failures = 0
    all_testcases = []

    for filepath in input_paths:
        if not Path(filepath).exists():
            print(f"Warning: {filepath} not found, skipping")
            continue

        device = extract_device_name(filepath)

        try:
            tree = ET.parse(filepath)
            root = tree.getroot()
        except ET.ParseError as e:
            print(f"Warning: Failed to parse {filepath}: {e}")
            continue

        # Find all testcase elements (handle both testsuite and testsuites root)
        testcases = root.findall('.//testcase')

        file_tests = len(testcases)
        file_failures = sum(1 for tc in testcases if tc.find('failure') is not None)

        print(f"  {device}: {file_tests - file_failures}/{file_tests} passed")

        for tc in testcases:
            # Add device prefix to classname
            classname = tc.get('classname', 'UniText')
            tc.set('classname', f"{device}.{classname}")
            all_testcases.append(tc)

        total_tests += file_tests
        total_failures += file_failures

    # Create merged XML
    root = ET.Element('testsuite')
    root.set('name', 'UniText')
    root.set('tests', str(total_tests))
    root.set('failures', str(total_failures))

    for tc in all_testcases:
        root.append(tc)

    tree = ET.ElementTree(root)
    ET.indent(tree, space='  ')
    tree.write(output_path, encoding='unicode', xml_declaration=True)

    return total_tests, total_failures


def main():
    if len(sys.argv) < 3:
        print(f"Usage: {sys.argv[0]} <output.xml> <input1.xml> [input2.xml ...]")
        sys.exit(1)

    output_path = sys.argv[1]
    input_paths = sys.argv[2:]

    print(f"Merging {len(input_paths)} result file(s)...")

    total_tests, total_failures = merge_results(output_path, input_paths)
    passed = total_tests - total_failures

    print(f"\nTotal: {passed}/{total_tests} passed")

    if total_tests == 0:
        print("ERROR: No test results found")
        sys.exit(1)

    if total_failures > 0:
        print(f"FAILED: {total_failures} test(s) failed")
        sys.exit(1)

    print("All tests passed")


if __name__ == '__main__':
    main()
