#!/bin/zsh
# Zips Builds/Mac/CargoClash.app with the README into the file to hand out.
# Run CargoClashPlayerBuild.Mac first.
#
#     Tools/package_mac.sh [output folder]
#
# The output folder defaults to Builds. ditto keeps the app's signature and permissions, which zip can lose.
set -e

root="${0:A:h:h}"
output="${1:-$root/Builds}"
app="$root/Builds/Mac/CargoClash.app"
name="CargoClash_Mac"
zip_path="${output:A}/${name}_$(date +%Y-%m-%d).zip"

if [[ ! -d "$app" ]]; then
    print -u2 "No Mac build in $app."
    exit 1
fi
codesign --verify --deep --strict "$app"

staging="$(mktemp -d)"
trap 'rm -rf "$staging"' EXIT
mkdir -p "$staging/$name" "$output"
ditto "$app" "$staging/$name/CargoClash.app"
cp "$root/Tools/mac_readme.txt" "$staging/$name/README.txt"

rm -f "$zip_path"
ditto -c -k --norsrc --noextattr --keepParent "$staging/$name" "$zip_path"
print "Wrote $zip_path"
