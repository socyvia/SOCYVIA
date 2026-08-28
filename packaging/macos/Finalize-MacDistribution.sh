#!/bin/bash
set -euo pipefail

usage() {
  echo "Usage: $0 <arm64|x64|all> [release-directory]" >&2
  echo "Optional environment: SOCYVIA_CODESIGN_IDENTITY, SOCYVIA_NOTARY_PROFILE" >&2
  exit 2
}

[[ $# -ge 1 && $# -le 2 ]] || usage
target="$1"
release_dir="${2:-$(cd "$(dirname "$0")/../../Download/macOS" && pwd)}"
script_dir="$(cd "$(dirname "$0")" && pwd)"
background="$script_dir/socyvia-dmg-background.png"
[[ -f "$background" ]] || { echo "Missing DMG background: $background" >&2; exit 1; }
command -v hdiutil >/dev/null || { echo "hdiutil is required on macOS." >&2; exit 1; }
command -v ditto >/dev/null || { echo "ditto is required on macOS." >&2; exit 1; }

build_one() {
  local arch="$1" label="$2" zip="$3" dmg="$4"
  local work mount_device mount_path rw_image
  work="$(mktemp -d "${TMPDIR:-/tmp}/socyvia-dmg.XXXXXX")"
  mount_path="$work/mount"
  rw_image="$work/SOCYVIA-rw.dmg"
  mkdir -p "$work/source" "$mount_path"
  trap '[[ -n "${mount_device:-}" ]] && hdiutil detach "$mount_device" -force >/dev/null 2>&1 || true; rm -rf "$work"' RETURN

  ditto -x -k "$release_dir/$zip" "$work/source"
  local app="$work/source/SOCYVIA.app"
  [[ -x "$app/Contents/MacOS/SOCYVIA" ]] || { echo "$zip does not contain an executable SOCYVIA.app." >&2; exit 1; }
  /usr/bin/file "$app/Contents/MacOS/SOCYVIA" | grep -q "$arch" || {
    echo "$zip contains the wrong executable architecture; expected $arch." >&2; exit 1;
  }

  if [[ -n "${SOCYVIA_CODESIGN_IDENTITY:-}" ]]; then
    codesign --force --deep --options runtime --timestamp --sign "$SOCYVIA_CODESIGN_IDENTITY" "$app"
    codesign --verify --deep --strict --verbose=2 "$app"
    rm -f "$release_dir/$zip"
    (cd "$work/source" && ditto -c -k --sequesterRsrc --keepParent SOCYVIA.app "$release_dir/$zip")
  else
    echo "$label remains unsigned (SOCYVIA_CODESIGN_IDENTITY is not set)."
  fi

  hdiutil create -quiet -ov -size 180m -fs HFS+ -volname "SOCYVIA 1.0.0" -format UDRW "$rw_image"
  mount_device="$(hdiutil attach -readwrite -noverify -noautoopen -mountpoint "$mount_path" "$rw_image" | awk '/Apple_HFS/ {print $1; exit}')"
  [[ -n "$mount_device" ]] || { echo "Unable to attach working disk image." >&2; exit 1; }
  ditto "$app" "$mount_path/SOCYVIA.app"
  ln -s /Applications "$mount_path/Applications"
  mkdir -p "$mount_path/.background"
  cp "$background" "$mount_path/.background/socyvia-dmg-background.png"

  osascript <<OSA
tell application "Finder"
  tell disk "SOCYVIA 1.0.0"
    open
    set current view of container window to icon view
    set toolbar visible of container window to false
    set statusbar visible of container window to false
    set bounds of container window to {160, 160, 920, 620}
    set arrangement of icon view options of container window to not arranged
    set icon size of icon view options of container window to 112
    set background picture of icon view options of container window to file ".background:socyvia-dmg-background.png"
    set position of item "SOCYVIA.app" of container window to {210, 282}
    set position of item "Applications" of container window to {550, 282}
    update without registering applications
    delay 2
    close
  end tell
end tell
OSA
  sync
  hdiutil detach "$mount_device" -quiet
  mount_device=""
  rm -f "$release_dir/$dmg"
  hdiutil convert -quiet "$rw_image" -format UDZO -imagekey zlib-level=9 -o "$release_dir/$dmg"
  hdiutil verify "$release_dir/$dmg" >/dev/null

  if [[ -n "${SOCYVIA_CODESIGN_IDENTITY:-}" ]]; then
    codesign --force --timestamp --sign "$SOCYVIA_CODESIGN_IDENTITY" "$release_dir/$dmg"
    codesign --verify --verbose=2 "$release_dir/$dmg"
  fi
  if [[ -n "${SOCYVIA_NOTARY_PROFILE:-}" ]]; then
    xcrun notarytool submit "$release_dir/$dmg" --keychain-profile "$SOCYVIA_NOTARY_PROFILE" --wait
    xcrun stapler staple "$release_dir/$dmg"
    xcrun stapler validate "$release_dir/$dmg"
  else
    echo "$label remains not notarized (SOCYVIA_NOTARY_PROFILE is not set)."
  fi
  echo "Created and verified $release_dir/$dmg"
}

mkdir -p "$release_dir"
case "$target" in
  arm64)
    build_one arm64 "Apple Silicon" "SOCYVIA-1.0.0-macOS-AppleSilicon-arm64.zip" "SOCYVIA-1.0.0-macOS-AppleSilicon-arm64.dmg" ;;
  x64)
    build_one x86_64 "Intel" "SOCYVIA-1.0.0-macOS-Intel-x64.zip" "SOCYVIA-1.0.0-macOS-Intel-x64.dmg" ;;
  all)
    build_one arm64 "Apple Silicon" "SOCYVIA-1.0.0-macOS-AppleSilicon-arm64.zip" "SOCYVIA-1.0.0-macOS-AppleSilicon-arm64.dmg"
    build_one x86_64 "Intel" "SOCYVIA-1.0.0-macOS-Intel-x64.zip" "SOCYVIA-1.0.0-macOS-Intel-x64.dmg" ;;
  *) usage ;;
esac
