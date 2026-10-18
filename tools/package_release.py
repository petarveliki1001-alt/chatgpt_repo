"""Package an already published build in versioned folders, without bundling .NET."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import zipfile


def main():
    root = Path(__file__).resolve().parents[1]
    version = ET.parse(root / 'PersonalMemoirsReader/PersonalMemoirsReader.csproj').findtext('./PropertyGroup/Version')
    if not version or not re.fullmatch(r'\d+\.\d+\.\d+', version):
        raise ValueError('Set a release version in PersonalMemoirsReader.csproj')
    published = root / f'artifacts/app-only-v{version}'
    files = sorted(p for p in published.rglob('*') if p.is_file() and p.suffix != '.pdb')
    expected = {'PersonalMemoirsReader.exe', 'PersonalMemoirsReader.dll',
                'PersonalMemoirsReader.deps.json', 'PersonalMemoirsReader.runtimeconfig.json',
                'f5_worker.py', 'f5_setup_worker.py', 'chatterbox_worker.py', 'voice_color_worker.py'}
    if {p.relative_to(published).as_posix() for p in files} != expected:
        raise ValueError('Publish an app-only build first; unexpected or missing published files')
    for name in expected:
        if name.endswith('.py') and (published/name).read_bytes() != (root/'PersonalMemoirsReader'/name).read_bytes():
            raise ValueError(f'Stale published worker: {name}')
    out = root / 'downloads'
    out.mkdir(exist_ok=True)
    app = out / f'PersonalMemoirsReader_v{version}_app-only.zip'
    source = out / f'PersonalMemoirsReader_v{version}_source.zip'
    if app.exists() or source.exists():
        raise FileExistsError('This version is already packaged. Increment the version for a new iteration.')
    with zipfile.ZipFile(app, 'x', zipfile.ZIP_DEFLATED) as z:
        for file in files:
            z.write(file, Path(app.stem) / file.relative_to(published))
        z.writestr(f'{app.stem}/START-HERE.txt',
                   f'Personal Memoirs Reader v{version}\n'
                   'Extract this versioned folder intact. Run PersonalMemoirsReader.exe on Windows x64.\n'
                   'Requires Microsoft .NET 8 Desktop Runtime x64.\n'
                   'Blank reference transcript uses automatic transcription. A legacy speech-only installation\n'
                   'will offer confirmed model setup before generation. Setup requires internet access and\n'
                   'downloads the additional Whisper model; later generation remains offline.\n'
                   'Version folders separate program files. Voice data, narrations, and Python/model caches remain shared.\n'
                   'Actual Windows setup and neural inference have not been verified in this Linux build environment.\n')
    source_files = [root/'README.md', root/'.gitignore', root/'requirements-test.txt', root/'PersonalMemoirsReader.sln']
    for folder in ['PersonalMemoirsReader', 'tests', 'tools']:
        source_files.extend(p for p in (root/folder).rglob('*') if p.is_file()
                            and not any(part in {'bin', 'obj', '__pycache__', '.venv'} for part in p.relative_to(root).parts))
    with zipfile.ZipFile(source, 'x', zipfile.ZIP_DEFLATED) as z:
        for file in sorted(source_files):
            z.write(file, Path(source.stem) / file.relative_to(root))
    for archive in [app, source]:
        with zipfile.ZipFile(archive) as z:
            assert z.testzip() is None
            assert all(name.startswith(archive.stem + '/') for name in z.namelist())
            print(f'{archive.name}: {len(z.namelist())} files; folder {archive.stem}/')


if __name__ == '__main__':
    main()
