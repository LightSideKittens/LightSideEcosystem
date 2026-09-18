const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const CORE_LICENSE_NAME = 'LICENSE-LightSide.Core.md';
const CORE_LICENSE_GUID = '470400b9844b872479ddbd2295b41add';
const CORE_LICENSE_META = `fileFormatVersion: 2
guid: ${CORE_LICENSE_GUID}
TextScriptImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
`;

function hideSamples(unitextDir) {
    execFileSync('node', [path.join(unitextDir, 'tools~', 'samples-pack.js'), 'hide', unitextDir], { stdio: 'inherit' });
}

function dropUnitextLicense(unitextDir) {
    fs.rmSync(path.join(unitextDir, 'LICENSE.md'), { force: true });
    fs.rmSync(path.join(unitextDir, 'LICENSE.md.meta'), { force: true });

    const manifestPath = path.join(unitextDir, 'package.json');
    const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
    delete manifest.license;
    fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 4) + '\n');

    const readmePath = path.join(unitextDir, 'README.md');
    const readme = fs.readFileSync(readmePath, 'utf8');
    fs.writeFileSync(readmePath, readme.replace(/## [^\r\n]* License\r?\n[\s\S]*?(?=## [^\r\n]* Third-Party)/, ''));
}

function renameCoreLicense(coreDir) {
    const source = path.join(coreDir, 'LICENSE.md');
    const target = path.join(coreDir, CORE_LICENSE_NAME);

    if (fs.existsSync(source)) fs.renameSync(source, target);
    fs.rmSync(source + '.meta', { force: true });

    if (fs.existsSync(target)) fs.writeFileSync(target + '.meta', CORE_LICENSE_META);
}

function verify(unitextDir, coreDir) {
    const problems = [];

    if (!fs.existsSync(path.join(unitextDir, 'Samples~'))) problems.push(`${unitextDir}/Samples~ is missing`);
    if (fs.existsSync(path.join(unitextDir, 'Samples'))) problems.push(`${unitextDir}/Samples is still visible`);
    if (fs.existsSync(path.join(unitextDir, 'LICENSE.md'))) problems.push(`${unitextDir}/LICENSE.md was not removed`);
    if (fs.existsSync(path.join(coreDir, 'LICENSE.md'))) problems.push(`${coreDir}/LICENSE.md was not renamed`);

    const coreLicense = path.join(coreDir, CORE_LICENSE_NAME);
    if (!fs.existsSync(coreLicense)) problems.push(`${coreLicense} is missing`);
    else if (!fs.existsSync(coreLicense + '.meta')) problems.push(`${coreLicense}.meta is missing`);

    if (JSON.parse(fs.readFileSync(path.join(unitextDir, 'package.json'), 'utf8')).license !== undefined) {
        problems.push(`${unitextDir}/package.json still declares a license`);
    }

    if (problems.length > 0) {
        throw new Error(`Asset Store transform did not reach the expected state:\n  ${problems.join('\n  ')}`);
    }
}

function apply(unitextDir, coreDir) {
    if (!fs.existsSync(path.join(unitextDir, 'package.json'))) throw new Error(`Not a package directory: ${unitextDir}`);
    if (!fs.existsSync(path.join(coreDir, 'package.json'))) throw new Error(`Not a package directory: ${coreDir}`);

    hideSamples(unitextDir);
    dropUnitextLicense(unitextDir);
    renameCoreLicense(coreDir);
    verify(unitextDir, coreDir);
}

module.exports = { apply, CORE_LICENSE_NAME, CORE_LICENSE_GUID };

if (require.main === module) {
    const [unitextDir, coreDir] = process.argv.slice(2);

    if (!unitextDir || !coreDir) {
        console.error('Usage: node transform.js <unitextDir> <coreDir>');
        process.exit(1);
    }

    try {
        apply(path.resolve(unitextDir), path.resolve(coreDir));
        console.log('Asset Store transform applied.');
    } catch (e) {
        console.error(e.message);
        process.exit(1);
    }
}
