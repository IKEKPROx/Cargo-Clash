#!/bin/zsh
# Zips Builds/Windows into the file to hand out, with the README and without Unity's do-not-ship folder.
# Run CargoClashPlayerBuild.Windows first.
#
#     Tools/package_windows.sh [output folder]
#
# The output folder defaults to Builds.
set -e

root="${0:A:h:h}"
output="${1:-$root/Builds}"
build="$root/Builds/Windows"
name="CargoClash_Windows"
zip_path="${output:A}/${name}_$(date +%Y-%m-%d).zip"

if [[ ! -f "$build/CargoClash.exe" ]]; then
    print -u2 "No Windows build in $build."
    exit 1
fi

staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
mkdir -p "$staging/$name" "$output"
ditto "$build" "$staging/$name"
find "$staging/$name" -maxdepth 1 -type d \( -name '*DontShipItWithYourGame' -o -name '*DoNotShip' \) -exec rm -rf {} +
cp "$root/Tools/windows_readme.txt" "$staging/$name/README.txt"

rm -f "$zip_path"
(cd "$staging" && zip -q -r -X "$zip_path" "$name")
print "Wrote $zip_path"
