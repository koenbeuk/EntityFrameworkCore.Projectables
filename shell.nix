{pkgs ? import <nixpkgs> {}}: let
  dotnet = with pkgs.dotnetCorePackages;
    combinePackages [
      sdk_7_0
      aspnetcore_7_0
    ];
in
  pkgs.mkShell {
    packages = [dotnet];

    DOTNET_ROOT = "${dotnet}";
  }
