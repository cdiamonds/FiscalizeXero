#!/bin/bash
# Builds versioned deliverables into releases/:
#   releases/FiscalizeXero-v<ver>.zip         (API, for IIS)
#   releases/FiscalizeXeroAngular-v<ver>.zip  (frontend, for cPanel)
# Usage: ./publish.sh [api|web|all]   (default: all)
# Each target bumps its own patch version. Commit the bump yourself (stage explicitly,
# not `git add -A`):  git add FiscalizeXero.Api/*.csproj FiscalizeXero.Angular/package*.json
set -e
cd "$(dirname "$0")"

TARGET="${1:-all}"
CSPROJ="FiscalizeXero.Api/FiscalizeXero.Api.csproj"
PUBLISH_BASE="publish"
RELEASES_DIR="releases"
mkdir -p "$RELEASES_DIR"
RELEASES_ABS="$(cd "$RELEASES_DIR" && pwd)"

publish_api() {
  CURRENT=$(grep -o '<Version>[^<]*</Version>' "$CSPROJ" | sed 's|<Version>\([^<]*\)</Version>|\1|' || true)
  [ -z "$CURRENT" ] && CURRENT="1.0.0"
  IFS='.' read -r MAJOR MINOR PATCH <<< "$CURRENT"
  NEW_VERSION="$MAJOR.$MINOR.$((PATCH + 1))"

  if grep -q '<Version>' "$CSPROJ"; then
    sed -i '' "s|<Version>.*</Version>|<Version>$NEW_VERSION</Version>|" "$CSPROJ"
  else
    sed -i '' "s|</PropertyGroup>|  <Version>$NEW_VERSION</Version>\n  </PropertyGroup>|" "$CSPROJ"
  fi
  echo "API version: $CURRENT -> $NEW_VERSION"

  rm -rf "$PUBLISH_BASE"
  dotnet publish "$CSPROJ" -c Release -o "$PUBLISH_BASE" --nologo

  # Neutralise shipped config: the server holds the real appsettings (connection string,
  # AuthSettings:Key, Xero credentials). Overwriting AuthSettings:Key signs out every user.
  for f in "$PUBLISH_BASE"/appsettings*.json; do
    [ -e "$f" ] || continue
    case "$(basename "$f")" in _*) continue;; esac
    mv "$f" "$(dirname "$f")/_$(basename "$f")"
  done

  ZIP="$RELEASES_ABS/FiscalizeXero-v$NEW_VERSION.zip"
  rm -f "$ZIP"
  (cd "$PUBLISH_BASE" && zip -r "$ZIP" . --exclude "*.zip" > /dev/null)
  echo "Zipped: $RELEASES_DIR/FiscalizeXero-v$NEW_VERSION.zip"
}

publish_web() {
  (
    cd FiscalizeXero.Angular
    npm version patch --no-git-tag-version > /dev/null
    VER=$(node -p "require('./package.json').version")
    echo "Angular version: -> $VER"
    npm run build:prod
    OUT=dist/FiscalizeXeroAngular/browser
    ZIP="$RELEASES_ABS/FiscalizeXeroAngular-v$VER.zip"
    rm -f "$ZIP"
    # Zip from INSIDE browser/ so index.html and .htaccess sit at the zip root.
    (cd "$OUT" && zip -r "$ZIP" . -x "*.zip" > /dev/null)
    unzip -l "$ZIP" | grep -q '\.htaccess' || { echo "ERROR: .htaccess missing from zip"; exit 1; }
    echo "Zipped: $RELEASES_DIR/FiscalizeXeroAngular-v$VER.zip"
  )
}

case "$TARGET" in
  api) publish_api ;;
  web) publish_web ;;
  all) publish_api; publish_web ;;
  *) echo "Usage: $0 [api|web|all]"; exit 1 ;;
esac
