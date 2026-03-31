#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PUBLISH_SCRIPT="$ROOT_DIR/scripts/publish.sh"

detect_runtime() {
  local os arch
  os="$(uname -s)"
  arch="$(uname -m)"

  case "$os" in
    Darwin) os="osx" ;;
    Linux) os="linux" ;;
    *)
      echo "Unsupported host OS: $os" >&2
      exit 1
      ;;
  esac

  case "$arch" in
    arm64|aarch64) arch="arm64" ;;
    x86_64) arch="x64" ;;
    *)
      echo "Unsupported host architecture: $arch" >&2
      exit 1
      ;;
  esac

  printf '%s-%s\n' "$os" "$arch"
}

copy_directory() {
  local source_dir="$1"
  local target_dir="$2"

  rm -rf "$target_dir"
  mkdir -p "$target_dir"

  if command -v rsync >/dev/null 2>&1; then
    rsync -a --delete "$source_dir"/ "$target_dir"/
    return
  fi

  cp -R "$source_dir"/. "$target_dir"/
}

write_manifest() {
  local stage_dir="$1"
  local runtime="$2"
  local artifact_name="$3"
  local commit_sha built_at

  commit_sha="$(git -C "$ROOT_DIR" rev-parse --short HEAD 2>/dev/null || printf '%s' 'unknown')"
  built_at="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

  cat > "$stage_dir/RELEASE-MANIFEST.json" <<EOF
{
  "name": "PrintHub",
  "runtime": "$runtime",
  "artifactName": "$artifact_name",
  "builtAtUtc": "$built_at",
  "gitCommit": "$commit_sha",
  "selfContained": true
}
EOF
}

package_release() {
  local release_root="$1"
  local stage_name="$2"
  local runtime="$3"
  local artifact_path

  case "$runtime" in
    osx-*)
      artifact_path="$release_root/$stage_name.zip"
      rm -f "$artifact_path"
      if command -v ditto >/dev/null 2>&1; then
        ditto -c -k --sequesterRsrc --keepParent "$release_root/$stage_name" "$artifact_path"
      else
        (
          cd "$release_root"
          zip -qry "$artifact_path" "$stage_name"
        )
      fi
      ;;
    *)
      artifact_path="$release_root/$stage_name.tar.gz"
      rm -f "$artifact_path"
      tar -czf "$artifact_path" -C "$release_root" "$stage_name"
      ;;
  esac

  shasum -a 256 "$artifact_path" > "$artifact_path.sha256"
  printf '%s\n' "$artifact_path"
}

RUNTIME="${1:-$(detect_runtime)}"
RELEASE_ROOT="${2:-$ROOT_DIR/output/release/$RUNTIME}"
PUBLISH_DIR="${PUBLISH_DIR:-$ROOT_DIR/output/publish/$RUNTIME}"
STAGE_NAME="PrintHub-$RUNTIME"
STAGE_DIR="$RELEASE_ROOT/$STAGE_NAME"
INCLUDE_PUBLISH="${INCLUDE_PUBLISH:-true}"

mkdir -p "$RELEASE_ROOT"

if [[ "$INCLUDE_PUBLISH" == "true" || ! -d "$PUBLISH_DIR" ]]; then
  bash "$PUBLISH_SCRIPT" "$RUNTIME" "$PUBLISH_DIR"
fi

if [[ ! -d "$PUBLISH_DIR" ]]; then
  echo "Publish output was not found: $PUBLISH_DIR" >&2
  exit 1
fi

copy_directory "$PUBLISH_DIR" "$STAGE_DIR/payload"
mkdir -p "$STAGE_DIR/docs"
cp "$ROOT_DIR/README.md" "$STAGE_DIR/docs/README.md"
cp "$ROOT_DIR/docs/user-guide.md" "$STAGE_DIR/docs/user-guide.md"
cp "$ROOT_DIR/docs/api.md" "$STAGE_DIR/docs/api.md"
write_manifest "$STAGE_DIR" "$RUNTIME" "$STAGE_NAME"

case "$RUNTIME" in
  osx-*)
    mkdir -p "$STAGE_DIR/Applications"
    bash "$STAGE_DIR/payload/install-printhub.sh" "$STAGE_DIR/Applications/PrintHub.app"
    ;;
esac

ARTIFACT_PATH="$(package_release "$RELEASE_ROOT" "$STAGE_NAME" "$RUNTIME")"

cat <<EOF

Release package completed.

Runtime:     $RUNTIME
Stage dir:   $STAGE_DIR
Artifact:    $ARTIFACT_PATH
Checksum:    $ARTIFACT_PATH.sha256

On macOS you can now optionally sign and notarize the staged app bundle with:
  $ROOT_DIR/scripts/release/macos/sign-macos-release.sh "$STAGE_DIR"
  $ROOT_DIR/scripts/release/macos/notarize-macos-release.sh "$STAGE_DIR" "$ARTIFACT_PATH"
EOF
