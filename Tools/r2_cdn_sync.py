import argparse
import getpass
import hashlib
import json
import mimetypes
import os
import shutil
import sys
from pathlib import Path

from tool_config import config_bool, config_value, load_config


def resolve_project_path(project_root, value):
    if value is None or str(value).strip() == "":
        return None

    path = Path(value)
    if path.is_absolute():
        return path.resolve()

    return (project_root / path).resolve()


def normalize_prefix(value):
    if value is None:
        return ""
    return str(value).strip().replace("\\", "/").strip("/")


def normalize_prefixes(value):
    if value is None:
        return []

    if isinstance(value, str):
        values = value.split(",")
    elif isinstance(value, (list, tuple)):
        values = value
    else:
        values = [value]

    prefixes = []
    for item in values:
        prefix = normalize_prefix(item)
        if prefix and prefix not in prefixes:
            prefixes.append(prefix)

    return prefixes


def select_prefix(config, cli_prefix):
    if cli_prefix is not None and str(cli_prefix).strip():
        return normalize_prefix(cli_prefix)

    prefixes = normalize_prefixes(config.get("Prefixes"))
    if not prefixes:
        prefixes = normalize_prefixes(config.get("Prefix"))

    if not prefixes:
        return ""

    if len(prefixes) == 1:
        return prefixes[0]

    if not sys.stdin.isatty():
        raise RuntimeError("Multiple R2 prefixes are configured. Pass --prefix to choose one.")

    print("Select R2 prefix:")
    for index, prefix in enumerate(prefixes, start=1):
        print(f"  {index}. {prefix}")

    while True:
        answer = input(f"Prefix [1-{len(prefixes)}, default 1]: ").strip()
        if answer == "":
            return prefixes[0]

        if answer.isdigit():
            selected_index = int(answer)
            if 1 <= selected_index <= len(prefixes):
                return prefixes[selected_index - 1]

        selected_prefix = normalize_prefix(answer)
        if selected_prefix in prefixes:
            return selected_prefix

        print("Invalid prefix. Enter a number from the list or one of the configured prefix names.")


def is_placeholder(value):
    if value is None:
        return True
    text = str(value).strip()
    return text == "" or text.startswith("your-")


def format_bytes(size):
    value = float(size)
    for unit in ("B", "KB", "MB", "GB"):
        if value < 1024 or unit == "GB":
            if unit == "B":
                return f"{int(value)} {unit}"
            return f"{value:.2f}".rstrip("0").rstrip(".") + f" {unit}"
        value /= 1024
    return f"{size} B"


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


def resolve_package_source(project_root, build_output_root, platform, package_name):
    package_root = resolve_project_path(project_root, build_output_root) / platform / package_name
    return find_latest_package_directory(package_root), platform, package_name


def publish_local_cdn(project_root, publish_config_path, cdn_root_override=None):
    config = load_config(publish_config_path)
    cdn_root = resolve_project_path(project_root, cdn_root_override or config_value(config, "CdnRootDirectory", default="LocalCdn"))
    build_output_root = config_value(config, "BuildOutputRoot", default="Bundles")
    platform = str(config_value(config, "Platform", default="Android")).strip() or "Android"
    package_name = str(config_value(config, "PackageName", default="DefaultPackage")).strip() or "DefaultPackage"
    clean_destination = config_bool(config, "CleanDestination", False)

    source, platform, package_name = resolve_package_source(project_root, build_output_root, platform, package_name)

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


def import_boto3():
    try:
        import boto3
        from botocore.config import Config
    except ImportError as exc:
        raise RuntimeError(
            "Python package 'boto3' is not installed. Run: python -m pip install -r Tools/requirements-r2.txt"
        ) from exc

    return boto3, Config


def ensure_r2_credentials(interactive):
    has_access_key = bool(os.environ.get("AWS_ACCESS_KEY_ID"))
    has_secret_key = bool(os.environ.get("AWS_SECRET_ACCESS_KEY"))

    if has_access_key and has_secret_key:
        return

    if not interactive:
        raise RuntimeError(
            "R2 credentials are missing. Set AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY, "
            "or remove --no-interactive-credentials to input them interactively."
        )

    print("R2 S3 API credentials are not set in this terminal.")
    if not has_access_key:
        access_key = input("R2 Access Key ID: ").strip()
        if not access_key:
            raise RuntimeError("R2 Access Key ID is empty.")
        os.environ["AWS_ACCESS_KEY_ID"] = access_key

    if not has_secret_key:
        secret_key = getpass.getpass("R2 Secret Access Key: ").strip()
        if not secret_key:
            raise RuntimeError("R2 Secret Access Key is empty.")
        os.environ["AWS_SECRET_ACCESS_KEY"] = secret_key


def create_s3_client(endpoint_url, aws_profile, interactive_credentials):
    boto3, config_type = import_boto3()
    if not aws_profile:
        ensure_r2_credentials(interactive_credentials)

    session_kwargs = {}
    if aws_profile:
        session_kwargs["profile_name"] = aws_profile

    session = boto3.Session(**session_kwargs)
    return session.client(
        "s3",
        endpoint_url=endpoint_url,
        region_name="auto",
        config=config_type(signature_version="s3v4"),
    )


def content_type_for(path):
    if path.suffix in {".version", ".hash"}:
        return "text/plain"
    if path.suffix == ".json":
        return "application/json"
    value, _ = mimetypes.guess_type(path.name)
    return value or "application/octet-stream"


def collect_local_files(cdn_root):
    files = []
    for path in cdn_root.rglob("*"):
        if not path.is_file():
            continue
        relative_path = path.relative_to(cdn_root).as_posix()
        files.append((path, relative_path))
    files.sort(key=lambda item: item[1])
    return files


def find_version_test_path(cdn_root):
    version_files = []
    for path in cdn_root.rglob("*.version"):
        if path.is_file():
            version_files.append(path)

    if not version_files:
        return ""

    version_files.sort(key=lambda item: item.stat().st_mtime, reverse=True)
    return version_files[0].relative_to(cdn_root).as_posix()


def file_md5(path):
    hasher = hashlib.md5()
    with path.open("rb") as file:
        for chunk in iter(lambda: file.read(1024 * 1024), b""):
            hasher.update(chunk)
    return hasher.hexdigest()


def build_local_manifest(local_files):
    manifest = {
        "format": 1,
        "files": {},
    }

    for path, relative_path in local_files:
        manifest["files"][relative_path] = {
            "size": path.stat().st_size,
            "md5": file_md5(path),
        }

    return manifest


def build_s3_key(prefix, relative_path):
    if prefix:
        return f"{prefix}/{relative_path}"
    return relative_path


def list_remote_objects(s3, bucket_name, prefix):
    objects = {}
    paginator = s3.get_paginator("list_objects_v2")
    kwargs = {"Bucket": bucket_name}
    if prefix:
        kwargs["Prefix"] = f"{prefix}/"

    for page in paginator.paginate(**kwargs):
        for item in page.get("Contents", []):
            objects[item["Key"]] = {
                "size": item.get("Size", 0),
                "etag": str(item.get("ETag", "")).strip('"').lower(),
            }

    return objects


def is_missing_object_error(exc):
    response = getattr(exc, "response", {})
    code = str(response.get("Error", {}).get("Code", ""))
    return code in {"NoSuchKey", "404", "NotFound"}


def read_remote_manifest(s3, bucket_name, manifest_key):
    if not manifest_key:
        return None

    try:
        response = s3.get_object(Bucket=bucket_name, Key=manifest_key)
        data = response["Body"].read()
        manifest = json.loads(data.decode("utf-8"))
    except Exception as exc:
        if is_missing_object_error(exc):
            return None
        raise

    if not isinstance(manifest, dict) or not isinstance(manifest.get("files"), dict):
        return None

    return manifest


def can_trust_etag(etag):
    return bool(etag) and "-" not in etag


def select_upload_files(local_files, local_manifest, remote_manifest, remote_objects, prefix, incremental_upload):
    if not incremental_upload:
        total_bytes = sum(path.stat().st_size for path, _ in local_files)
        return list(local_files), 0, 0, total_bytes, "disabled"

    upload_files = []
    upload_bytes = 0
    skipped_count = 0
    skipped_bytes = 0
    local_file_infos = local_manifest["files"]

    if remote_manifest:
        remote_file_infos = remote_manifest["files"]
        compare_source = "manifest"
        for path, relative_path in local_files:
            local_info = local_file_infos[relative_path]
            remote_info = remote_file_infos.get(relative_path)
            if (remote_info and
                    int(remote_info.get("size", -1)) == local_info["size"] and
                    str(remote_info.get("md5", "")).lower() == local_info["md5"]):
                skipped_count += 1
                skipped_bytes += local_info["size"]
                continue

            upload_files.append((path, relative_path))
            upload_bytes += local_info["size"]

        return upload_files, skipped_count, skipped_bytes, upload_bytes, compare_source

    compare_source = "remote objects"
    for path, relative_path in local_files:
        local_info = local_file_infos[relative_path]
        remote_info = remote_objects.get(build_s3_key(prefix, relative_path))
        if remote_info:
            remote_etag = str(remote_info.get("etag", "")).lower()
            if (int(remote_info.get("size", -1)) == local_info["size"] and
                    can_trust_etag(remote_etag) and
                    remote_etag == local_info["md5"]):
                skipped_count += 1
                skipped_bytes += local_info["size"]
                continue

        upload_files.append((path, relative_path))
        upload_bytes += local_info["size"]

    return upload_files, skipped_count, skipped_bytes, upload_bytes, compare_source


def upload_manifest(s3, bucket_name, manifest_key, manifest, remote_manifest, dry_run):
    if not manifest_key:
        return 0, "disabled"

    body = json.dumps(manifest, indent=2, sort_keys=True).encode("utf-8")
    if remote_manifest == manifest:
        print(f"Sync manifest unchanged s3://{bucket_name}/{manifest_key}")
        return 0, "unchanged"

    if dry_run:
        print(f"DRYRUN upload sync manifest -> s3://{bucket_name}/{manifest_key} ({format_bytes(len(body))})")
        return len(body), "dry-run"

    s3.put_object(
        Bucket=bucket_name,
        Key=manifest_key,
        Body=body,
        ContentType="application/json",
    )
    print(f"Uploaded sync manifest s3://{bucket_name}/{manifest_key} ({format_bytes(len(body))})")
    return len(body), "uploaded"


def delete_remote_keys(s3, bucket_name, keys, dry_run):
    if not keys:
        return 0

    sorted_keys = sorted(keys)
    for start in range(0, len(sorted_keys), 1000):
        batch = sorted_keys[start:start + 1000]
        if dry_run:
            for key in batch:
                print(f"DRYRUN delete s3://{bucket_name}/{key}")
            continue

        s3.delete_objects(
            Bucket=bucket_name,
            Delete={"Objects": [{"Key": key} for key in batch], "Quiet": True},
        )

    return len(sorted_keys)


def upload_files(s3, bucket_name, prefix, local_files, dry_run):
    uploaded_count = 0
    uploaded_bytes = 0

    for path, relative_path in local_files:
        key = build_s3_key(prefix, relative_path)
        size = path.stat().st_size
        uploaded_count += 1
        uploaded_bytes += size

        if dry_run:
            print(f"DRYRUN upload {path} -> s3://{bucket_name}/{key} ({format_bytes(size)})")
            continue

        s3.upload_file(
            str(path),
            bucket_name,
            key,
            ExtraArgs={"ContentType": content_type_for(path)},
        )
        print(f"Uploaded s3://{bucket_name}/{key} ({format_bytes(size)})")

    return uploaded_count, uploaded_bytes


def join_url(*parts):
    clean_parts = []
    for part in parts:
        if part is None or str(part).strip() == "":
            continue
        clean_parts.append(str(part).strip().strip("/"))

    if not clean_parts:
        return ""

    if clean_parts[0].startswith(("http://", "https://")):
        root = clean_parts[0].rstrip("/")
        if len(clean_parts) == 1:
            return root
        return root + "/" + "/".join(clean_parts[1:])

    return "/".join(clean_parts)


def parse_args():
    parser = argparse.ArgumentParser(description="Sync local YooAsset CDN root to Cloudflare R2.")
    parser.add_argument("--config-path", default=None)
    parser.add_argument("--cdn-root-directory", default=None)
    parser.add_argument("--bucket-name", default=None)
    parser.add_argument("--account-id", default=None)
    parser.add_argument("--prefix", default=None, help="R2 object prefix to use. Overrides Prefixes in config.")
    parser.add_argument("--aws-profile", default=None)
    parser.add_argument("--delete-remote", action="store_true", default=None)
    parser.add_argument("--keep-remote", action="store_true", default=None)
    parser.add_argument("--publish-local-first", action="store_true", default=None)
    parser.add_argument("--skip-local-publish", action="store_true", default=None)
    parser.add_argument("--publish-config-path", default=None)
    parser.add_argument("--public-root", default=None)
    parser.add_argument("--dry-run", action="store_true", default=None)
    parser.add_argument("--incremental-upload", action="store_true", default=None)
    parser.add_argument("--upload-all", action="store_true", default=None)
    parser.add_argument("--sync-manifest-file-name", default=None)
    parser.add_argument("--interactive-credentials", action="store_true", default=None)
    parser.add_argument("--no-interactive-credentials", action="store_true", default=None)
    parser.add_argument("--pause-on-exit", action="store_true", default=None)
    parser.add_argument("--no-pause-on-exit", action="store_true", default=None)
    return parser.parse_args()


def get_config_path(script_dir, project_root, args):
    if args.config_path:
        return resolve_project_path(project_root, args.config_path)

    return script_dir / "r2_cdn_sync.env"


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

    cdn_root_directory = config_value(config, "CdnRootDirectory", args.cdn_root_directory, "LocalCdn")
    bucket_name = str(config_value(config, "BucketName", args.bucket_name, "")).strip()
    account_id = str(config_value(config, "AccountId", args.account_id, "")).strip()
    prefix = select_prefix(config, args.prefix)
    aws_profile = str(config_value(config, "AwsProfile", args.aws_profile, "")).strip()
    publish_config_path = config_value(config, "PublishConfigPath", args.publish_config_path, "Tools/local_cdn_server.env")
    public_root = str(config_value(config, "PublicRoot", args.public_root, "")).strip()
    version_test_path = ""
    sync_manifest_file_name = str(config_value(config, "SyncManifestFileName", args.sync_manifest_file_name, ".r2-sync-manifest.json")).strip().replace("\\", "/").strip("/")

    delete_remote = config_bool(config, "DeleteRemote", False)
    if args.delete_remote:
        delete_remote = True
    if args.keep_remote:
        delete_remote = False

    publish_local_first = config_bool(config, "PublishLocalFirst", True)
    if args.publish_local_first:
        publish_local_first = True
    if args.skip_local_publish:
        publish_local_first = False

    dry_run = config_bool(config, "DryRun", False)
    if args.dry_run:
        dry_run = True

    incremental_upload = config_bool(config, "IncrementalUpload", True)
    if args.incremental_upload:
        incremental_upload = True
    if args.upload_all:
        incremental_upload = False

    interactive_credentials = config_bool(config, "InteractiveCredentials", True)
    if args.interactive_credentials:
        interactive_credentials = True
    if args.no_interactive_credentials:
        interactive_credentials = False

    if is_placeholder(bucket_name):
        raise RuntimeError(f"R2 bucket name is empty. Edit {config_path} or pass --bucket-name.")

    if is_placeholder(account_id):
        raise RuntimeError(f"R2 account id is empty. Edit {config_path} or pass --account-id.")

    endpoint_url = f"https://{account_id}.r2.cloudflarestorage.com"

    s3 = create_s3_client(endpoint_url, aws_profile, interactive_credentials)
    cdn_root = resolve_project_path(project_root, cdn_root_directory)

    if publish_local_first:
        publish_config_full_path = resolve_project_path(project_root, publish_config_path)
        print("Publish local CDN first:")
        print(f"  Config: {publish_config_full_path}")
        publish_local_cdn(project_root, publish_config_full_path, cdn_root)
        print()

    if not cdn_root.exists():
        raise RuntimeError(f"CDN root directory does not exist: {cdn_root}")

    if not version_test_path:
        version_test_path = find_version_test_path(cdn_root)

    local_files = collect_local_files(cdn_root)
    local_keys = {build_s3_key(prefix, relative_path) for _, relative_path in local_files}
    manifest_key = build_s3_key(prefix, sync_manifest_file_name) if incremental_upload and sync_manifest_file_name else ""
    if manifest_key:
        local_keys.add(manifest_key)

    print("Sync CDN root to Cloudflare R2:")
    print(f"  Source:   {cdn_root}")
    print(f"  Target:   s3://{bucket_name}/{prefix}" if prefix else f"  Target:   s3://{bucket_name}")
    print(f"  Endpoint: {endpoint_url}")
    print(f"  Delete:   {delete_remote}")
    print(f"  DryRun:   {dry_run}")
    print(f"  Incremental upload: {incremental_upload}")
    if manifest_key:
        print(f"  Sync manifest: s3://{bucket_name}/{manifest_key}")
    if aws_profile:
        print(f"  Profile:  {aws_profile}")
    print()

    remote_objects = {}
    if incremental_upload or delete_remote:
        remote_objects = list_remote_objects(s3, bucket_name, prefix)

    local_manifest = build_local_manifest(local_files) if incremental_upload else None
    remote_manifest = read_remote_manifest(s3, bucket_name, manifest_key) if manifest_key else None
    if incremental_upload:
        print(f"Incremental compare source: {'remote sync manifest' if remote_manifest else 'remote object list'}")

    deleted_count = 0
    if delete_remote:
        remote_keys = set(remote_objects.keys())
        deleted_count = delete_remote_keys(s3, bucket_name, remote_keys - local_keys, dry_run)

    files_to_upload, skipped_count, skipped_bytes, upload_bytes, compare_source = select_upload_files(
        local_files,
        local_manifest,
        remote_manifest,
        remote_objects,
        prefix,
        incremental_upload,
    )

    uploaded_count, uploaded_bytes = upload_files(s3, bucket_name, prefix, files_to_upload, dry_run)
    if incremental_upload:
        manifest_bytes, manifest_status = upload_manifest(s3, bucket_name, manifest_key, local_manifest, remote_manifest, dry_run)
    else:
        manifest_bytes, manifest_status = 0, "disabled"

    print()
    print("R2 sync completed.")
    print(f"  Compared by: {compare_source}")
    print(f"  Uploaded:    {uploaded_count} files, {format_bytes(uploaded_bytes)}")
    print(f"  Skipped:     {skipped_count} unchanged files, {format_bytes(skipped_bytes)}")
    print(f"  Deleted:  {deleted_count} remote files")
    if manifest_status == "unchanged":
        print("  Manifest:    unchanged")
    elif manifest_bytes:
        print(f"  Manifest:    {format_bytes(manifest_bytes)}")

    if public_root and "pub-xxxx" not in public_root and version_test_path:
        print("  Version test URL:")
        print(f"  {join_url(public_root, prefix, version_test_path)}")


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
