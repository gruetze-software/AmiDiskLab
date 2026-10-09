#!/usr/bin/env sh
set -eu

SOURCE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
APP_DIR="$HOME/.local/lib/AmiDiskLab"
DESKTOP_DIR="$HOME/.local/share/applications"
ICON_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"

mkdir -p "$APP_DIR" "$DESKTOP_DIR" "$ICON_DIR"
install -m 755 "$SOURCE_DIR/AmiDiskLab" "$APP_DIR/AmiDiskLab"
install -m 644 "$SOURCE_DIR/AmiDiskLab.png" "$ICON_DIR/AmiDiskLab.png"

cat > "$DESKTOP_DIR/AmiDiskLab.desktop" <<EOF
[Desktop Entry]
Type=Application
Version=1.4
Name=AmiDiskLab
GenericName=Amiga Disk & Software Manager
Comment=Manage Amiga disk images, archives, and software metadata
Exec=$APP_DIR/AmiDiskLab
Icon=AmiDiskLab
Terminal=false
Categories=Utility;Game;
StartupWMClass=AmiDiskLab
EOF

chmod 644 "$DESKTOP_DIR/AmiDiskLab.desktop"
printf '%s\n' "AmiDiskLab was installed for the current user."
