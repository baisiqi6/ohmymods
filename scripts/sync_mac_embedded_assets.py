#!/usr/bin/env python3
"""Wire an existing Mac wrapper to the resource list beside its source Compile glob.

Does not generate references, version metadata, PluginInfo, or deployment targets.
Run again after redirecting the source Compile glob to a canonical source copy;
--check-only rejects a stale import rather than accepting resources from old source.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET


class WiringError(ValueError):
    pass


def load_xml(text):
    root = ET.fromstring(text)
    if root.tag != 'Project':
        raise WiringError('Only unnamespaced MSBuild Project XML is supported')
    return root


def unconditional(root, nodes):
    parents = {child: parent for parent in root.iter() for child in parent}
    for node in nodes:
        while node is not None:
            if 'Condition' in node.attrib:
                raise WiringError('Conditional resource/source declarations are unsupported')
            node = parents.get(node)


def source_directory(root, wrapper):
    sources = [n for n in root.iter('Compile') if n.get('Include', '').replace('\\', '/').endswith('/*.cs')]
    if len(sources) != 1:
        raise WiringError('Expected one literal top-level source Compile glob ending in /*.cs')
    unconditional(root, sources)
    value = sources[0].get('Include').replace('\\', '/')[:-5]
    if any(token in value for token in ('$', '%', '@', '*', '?', ';')):
        raise WiringError('Source Compile directory must be a literal path')
    source = Path(value)
    return (source if source.is_absolute() else wrapper.parent / source).resolve()


def resource_manifest(props):
    root = load_xml(props.read_text())
    resources = list(root.iter('EmbeddedResource'))
    unconditional(root, resources)
    if len(root) != 1 or root[0].tag != 'ItemGroup' or len(root[0]) != len(resources) or not resources:
        raise WiringError('Expected a single ItemGroup containing only explicit EmbeddedResource items')
    result, names = [], set()
    for node in resources:
        if set(node.attrib) != {'Include', 'LogicalName'} or len(node):
            raise WiringError('Resource must have only Include and LogicalName attributes')
        include, name = node.get('Include'), node.get('LogicalName')
        prefix = '$(MSBuildThisFileDirectory)'
        if not include.startswith(prefix) or any(c in include[len(prefix):] for c in '$%@*?;'):
            raise WiringError('Resource Include must be a literal path relative to MSBuildThisFileDirectory')
        relative = Path(include[len(prefix):].replace('\\', '/'))
        if relative.is_absolute() or '..' in relative.parts:
            raise WiringError('Resource must stay under the shared props directory')
        if not name or name in names or any(c in name for c in '$%@;'):
            raise WiringError('Missing or duplicate resource LogicalName: ' + name)
        names.add(name)
        path = (props.parent / relative).resolve()
        if not path.is_file():
            raise WiringError('Missing resource file: ' + str(path))
        result.append({'LogicalName': name, 'FullPath': str(path), 'SHA256': hashlib.sha256(path.read_bytes()).hexdigest()})
    return result


def wire(wrapper, check_only=False):
    text = wrapper.read_text()
    root = load_xml(text)
    source = source_directory(root, wrapper)
    props = source / 'EmbeddedAssets.props'
    manifest = resource_manifest(props)
    resources = list(root.iter('EmbeddedResource'))
    unconditional(root, resources)
    for node in resources:
        if set(node.attrib) != {'Include', 'LogicalName'} or len(node):
            raise WiringError('Cannot safely replace a resource operation or child metadata')
    imports = [n for n in root.iter('Import') if 'EmbeddedAssets.props' in n.get('Project', '')]
    unconditional(root, imports)
    if any(set(n.attrib) != {'Project'} or len(n) for n in imports):
        raise WiringError('Unsupported resource Import')
    expected = str(props)
    if check_only:
        if resources or len(imports) != 1:
            raise WiringError('Wrapper must import shared resources exactly once and contain no inline EmbeddedResource')
        actual = imports[0].get('Project')
        if any(c in actual for c in '$%@*?;'):
            raise WiringError('Resource Import must be a literal path')
        actual_path = Path(actual)
        actual_path = (actual_path if actual_path.is_absolute() else wrapper.parent / actual_path).resolve()
        if actual_path != props:
            raise WiringError('Resource Import does not follow the source Compile directory')
        return text, manifest
    text, count = re.subn(r'<EmbeddedResource\b[^>]*?\s*/>', '', text)
    if count != len(resources):
        raise WiringError('Unsupported resource XML syntax; refusing partial replacement')
    # Only remove imports already targeting this shared resource file.
    text, count = re.subn(r'<Import\b(?=[^>]*\bProject=["\'][^"\']*EmbeddedAssets\.props["\'])[^>]*?\s*/>', '', text)
    if count != len(imports):
        raise WiringError('Unsupported Import XML syntax; refusing partial replacement')
    text = text.replace('</Project>', '  <Import Project="' + expected.replace('&', '&amp;').replace('"', '&quot;') + '" />\n</Project>')
    load_xml(text)
    return text, manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('wrapper', type=Path)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--output', type=Path, help='Write a migrated wrapper; input is never overwritten')
    mode.add_argument('--check-only', action='store_true', help='Reject inline or stale resources without writing')
    args = parser.parse_args()
    try:
        wrapper = args.wrapper.resolve()
        text, manifest = wire(wrapper, args.check_only)
        if args.output:
            output = args.output.resolve()
            if output == wrapper:
                raise WiringError('Output must differ from input wrapper')
            # Relative Compile/Reference/PluginInfo paths must retain their original base.
            if output.parent != wrapper.parent:
                raise WiringError('Output must be beside input wrapper to preserve relative paths')
            output.write_text(text)
        print(json.dumps({'ok': True, 'resourceCount': len(manifest), 'resources': manifest}, indent=2))
        return 0
    except (WiringError, OSError, ET.ParseError) as error:
        print('resource wiring rejected: ' + str(error), file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
