#!/usr/bin/env sh
set -eu

rm -f "$HOME/.local/share/applications/AmiDiskLab.desktop"
rm -f "$HOME/.local/share/icons/hicolor/256x256/apps/AmiDiskLab.png"
rm -rf "$HOME/.local/lib/AmiDiskLab"
printf '%s\n' "AmiDiskLab was removed from the current user profile."
