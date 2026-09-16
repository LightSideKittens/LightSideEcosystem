const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const mode = process.argv[2];

if (mode !== 'prepare' && mode !== 'restore') {
    console.error('Usage: node Tools/assetstore.js prepare|restore');
    process.exit(1);
}

const root = path.join(__dirname, '..');
const unitext = path.join(root, 'Packages', 'media.lightside.unitext');
const core = path.join(root, 'Packages', 'media.lightside.core');
const myspace = path.join(root, 'Assets', 'UniText_MySpace');
const stash = path.join(root, 'Library', 'LightSide', 'AssetStoreStash');
const packed = ['WebGLDemo', 'Slideshow'];
const coreLicense = path.join(core, 'LICENSE-LightSide.Core.md');

function hasFiles(dir) {
    return fs.readdirSync(dir, { withFileTypes: true })
        .some(e => e.isDirectory() ? hasFiles(path.join(dir, e.name)) : true);
}

function moveDir(from, to) {
    if (!fs.existsSync(from)) return;
    if (fs.existsSync(to)) {
        if (hasFiles(to)) {
            console.error(`Cannot move ${from} onto ${to}: destination already holds files`);
            process.exit(1);
        }
        fs.rmSync(to, { recursive: true });
    }
    fs.renameSync(from, to);
}

function moveFile(from, to) {
    if (fs.existsSync(from)) fs.renameSync(from, to);
}

function run(command, args, cwd) {
    execFileSync(command, args, { cwd, stdio: 'inherit' });
}

function samples(action) {
    run('node', [path.join(unitext, 'tools~', 'samples-pack.js'), action, unitext]);
}

if (mode === 'prepare') {
    samples('hide');

    fs.mkdirSync(stash, { recursive: true });
    for (const name of packed) {
        moveDir(path.join(myspace, name), path.join(stash, name));
        moveFile(path.join(myspace, name + '.meta'), path.join(stash, name + '.meta'));
    }

    fs.rmSync(path.join(unitext, 'LICENSE.md'), { force: true });
    fs.rmSync(path.join(unitext, 'LICENSE.md.meta'), { force: true });
    moveFile(path.join(core, 'LICENSE.md'), coreLicense);
    fs.rmSync(path.join(core, 'LICENSE.md.meta'), { force: true });

    const manifestPath = path.join(unitext, 'package.json');
    const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
    delete manifest.license;
    fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 4) + '\n');

    const readmePath = path.join(unitext, 'README.md');
    const readme = fs.readFileSync(readmePath, 'utf8');
    fs.writeFileSync(readmePath, readme.replace(/## [^\r\n]* License\r?\n[\s\S]*?(?=## [^\r\n]* Third-Party)/, ''));

    console.log('Done. Upload to Asset Store, then run: Tools\\assetstore-restore.bat');
} else {
    samples('show');

    for (const name of packed) {
        moveDir(path.join(stash, name), path.join(myspace, name));
        moveFile(path.join(stash, name + '.meta'), path.join(myspace, name + '.meta'));
    }

    fs.rmSync(coreLicense, { force: true });
    fs.rmSync(coreLicense + '.meta', { force: true });

    run('git', ['checkout', '--', 'LICENSE.md', 'LICENSE.md.meta', 'package.json', 'README.md'], unitext);
    run('git', ['checkout', '--', 'LICENSE.md', 'LICENSE.md.meta'], core);
    run('git', ['status', '--short'], unitext);
    run('git', ['status', '--short'], core);

    console.log('Restored. Both submodules must be clean above.');
}
