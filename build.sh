#!/bin/sh
# Builds PlanetCrafterModMenu.dll with the csc.exe that ships with Windows and installs it into the game.
cd "$(dirname "$0")"
G="C:/Program Files (x86)/Steam/steamapps/common/The Planet Crafter Demo"
M="$G/Planet Crafter_Data/Managed"
mkdir -p out
{
  echo "-nologo -nostdlib -target:library -optimize -nowarn:618,1701,1702"
  echo "-out:out/PlanetCrafterModMenu.dll"
  for d in mscorlib netstandard System System.Core Assembly-CSharp UnityEngine UnityEngine.CoreModule UnityEngine.UI UnityEngine.UIModule UnityEngine.IMGUIModule UnityEngine.TextRenderingModule UnityEngine.UnityWebRequestModule Unity.InputSystem Unity.TextMeshPro Unity.Netcode.Runtime; do
    echo "-r:\"$M/$d.dll\""
  done
  echo "-r:\"$G/BepInEx/core/BepInEx.dll\""
  echo "src\PlanetCrafterModMenu.cs"
} > out/build.rsp
/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe -noconfig @out/build.rsp || exit 1
mkdir -p "$G/BepInEx/plugins/planet-crafter-mod-menu"
cp out/PlanetCrafterModMenu.dll "$G/BepInEx/plugins/planet-crafter-mod-menu/" || { echo "install failed: close the game first (the DLL is in use)"; exit 1; }
echo installed
