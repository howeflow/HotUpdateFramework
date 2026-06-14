import argparse
import functools
import os
import shutil
import socket
import sys
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from tool_config import config_bool, config_value, load_config


def resolve_project_path(project_root, value):
    if value is None or str(value).strip() == "":
        return None

    path = Path(value)
    if path.is_absolute():
        return path.resolve()

    return (project_root / path).resolve()


def ensure_child_path(root, child):
    root_text = os.path.normcase(os.path.abspath(root))
    child_text = os.path.normcase(os.path.abspath(child))
    common = os.path.commonpath([root_text, child_text])
    if common != root_text:
        raise RuntimeError(f"Destination is outside CDN root: {child}")


def find_latest_package_directory(package_root):
    if not package_root.exists():
        raise RuntimeError(f"Package build root does not exist: {package_root}")

    candidates = []
    package_name = package_root.name
    version_file_name = f"{package_name}.version"
    for child in package_root.iterdir():
        if not child.is_dir():
            continue
        if child.name in {"OutputCache", "Simulate"}:
            continue
        if (child / version_file_name).exists():
            candidates.append(child)

    if not candidates:
        raise RuntimeError(f"Can not find package version directory under: {package_root}")

    candidates.sort(key=lambda item: item.stat().st_mtime, reverse=True)
    return candidates[0]


def resolve_package_source(project_root, platform, package_name):
    package_root = project_root / "Bundles" / platform / package_name
    return find_latest_package_directory(package_root), platform, package_name


def publish_local_cdn(
    project_root,
    config_path,
    cdn_root_directory=None,
    platform=None,
    package_name=None,
    clean_destination=None,
):
    config = load_config(config_path)

    cdn_root = resolve_project_path(project_root, config_value(config, "CdnRootDirectory", cdn_root_directory, "LocalCdn"))
    platform = str(config_value(config, "Platform", platform, "Android")).strip() or "Android"
    package_name = str(config_value(config, "PackageName", package_name, "DefaultPackage")).strip() or "DefaultPackage"

    if clean_destination is None:
        clean_destination = config_bool(config, "CleanDestination", False)

    source, platform, package_name = resolve_package_source(project_root, platform, package_name)

    destination = (cdn_root / platform / package_name).resolve()
    ensure_child_path(cdn_root, destination)

    if clean_destination and destination.exists():
        shutil.rmtree(destination)

    destination.mkdir(parents=True, exist_ok=True)
    for item in source.iterdir():
        target = destination / item.name
        if item.is_dir():
            shutil.copytree(item, target, dirs_exist_ok=True)
        else:
            shutil.copy2(item, target)

    print("Published YooAsset package:")
    print(f"  Source:      {source}")
    print(f"  Destination: {destination}")
    print(f"  Platform:    {platform}")
    print(f"  Package:     {package_name}")

    return destination


def get_lan_ip_addresses():
    addresses = []

    try:
        host_name = socket.gethostname()
        for item in socket.getaddrinfo(host_name, None, socket.AF_INET):
            ip = item[4][0]
            if ip.startswith("127.") or ip in addresses:
                continue
            addresses.append(ip)
    except OSError:
        pass

    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.connect(("8.8.8.8", 80))
            ip = sock.getsockname()[0]
            if ip and not ip.startswith("127.") and ip not in addresses:
                addresses.append(ip)
    except OSError:
        pass

    return addresses


def print_server_urls(bind_host, port, test_path):
    path = str(test_path or "").strip().strip("/")
    hosts = []

    if bind_host in {"", "0.0.0.0", "::"}:
        hosts.append("127.0.0.1")
        hosts.extend(get_lan_ip_addresses())
    else:
        hosts.append(bind_host)

    print("Local CDN server URLs:")
    for host in hosts:
        root_url = f"http://{host}:{port}"
        print(f"  {root_url}")
        if path:
            print(f"  {root_url}/{path}")


def start_local_server(cdn_root, host, port, test_path):
    handler = functools.partial(SimpleHTTPRequestHandler, directory=str(cdn_root))
    server = ThreadingHTTPServer((host, port), handler)
    actual_host, actual_port = server.server_address[:2]

    print()
    print("Local CDN server started:")
    print(f"  Directory: {cdn_root}")
    print(f"  Bind:      {actual_host}:{actual_port}")
    print_server_urls(host, actual_port, test_path)
    print()
    print("Press Ctrl+C to stop the local CDN server.")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print()
        print("Local CDN server stopped.")
    finally:
        server.server_close()


def parse_args():
    parser = argparse.ArgumentParser(description="Publish latest YooAsset package directory to a local CDN root.")
    parser.add_argument("--config-path", default=None)
    parser.add_argument("--cdn-root-directory", default=None)
    parser.add_argument("--platform", default=None)
    parser.add_argument("--package-name", default=None)
    parser.add_argument("--clean-destination", action="store_true", default=None)
    parser.add_argument("--keep-destination", action="store_true", default=None)
    parser.add_argument("--start-local-server", action="store_true", default=None)
    parser.add_argument("--no-start-local-server", action="store_true", default=None)
    parser.add_argument("--local-server-host", default=None)
    parser.add_argument("--local-server-port", type=int, default=None)
    parser.add_argument("--local-server-test-path", default=None)
    parser.add_argument("--pause-on-exit", action="store_true", default=None)
    parser.add_argument("--no-pause-on-exit", action="store_true", default=None)
    return parser.parse_args()


def get_config_path(script_dir, project_root, args):
    if args.config_path:
        return resolve_project_path(project_root, args.config_path)

    return script_dir / "local_cdn_server.env"


def get_pause_on_exit(config, args):
    pause_on_exit = config_bool(config, "PauseOnExit", False)
    if args.pause_on_exit:
        pause_on_exit = True
    if args.no_pause_on_exit:
        pause_on_exit = False
    return pause_on_exit


def pause_if_needed(enabled):
    if not enabled:
        return

    try:
        input("\nPress Enter to exit...")
    except EOFError:
        pass


def main(args):
    script_dir = Path(__file__).resolve().parent
    project_root = script_dir.parent
    config_path = get_config_path(script_dir, project_root, args)
    config = load_config(config_path)

    clean_destination = None
    if args.clean_destination:
        clean_destination = True
    if args.keep_destination:
        clean_destination = False

    destination = publish_local_cdn(
        project_root=project_root,
        config_path=config_path,
        cdn_root_directory=args.cdn_root_directory,
        platform=args.platform,
        package_name=args.package_name,
        clean_destination=clean_destination,
    )

    start_server = config_bool(config, "StartLocalServer", False)
    if args.start_local_server:
        start_server = True
    if args.no_start_local_server:
        start_server = False

    if start_server:
        cdn_root = destination.parent.parent
        host = str(config_value(config, "LocalServerHost", args.local_server_host, "0.0.0.0")).strip() or "0.0.0.0"
        port = int(config_value(config, "LocalServerPort", args.local_server_port, 8080))
        default_test_path = f"{destination.parent.name}/{destination.name}/{destination.name}.version"
        test_path = config_value(config, "LocalServerTestPath", args.local_server_test_path, default_test_path)
        start_local_server(cdn_root, host, port, test_path)


if __name__ == "__main__":
    args = parse_args()
    script_dir = Path(__file__).resolve().parent
    project_root = script_dir.parent
    config = load_config(get_config_path(script_dir, project_root, args))
    pause_on_exit = get_pause_on_exit(config, args)
    exit_code = 0

    try:
        main(args)
    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr)
        exit_code = 1
    finally:
        pause_if_needed(pause_on_exit)

    sys.exit(exit_code)
