// Builds the Linux packages (AppImage + .deb) with electron-builder, with the
// version of the Windows app (one version number for every platform).
const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const csproj = fs.readFileSync(path.join(__dirname, "..", "..", "src", "Navigatueur.App", "Navigatueur.App.csproj"), "utf8");
const version = /<Version>(\d+\.\d+\.\d+)<\/Version>/.exec(csproj)[1];
const pkgPath = path.join(__dirname, "..", "package.json");
const pkg = JSON.parse(fs.readFileSync(pkgPath, "utf8"));
pkg.version = version;
fs.writeFileSync(pkgPath, JSON.stringify(pkg, null, 2) + "\n");
console.log("Navigatueur", version);

const builder = path.join(__dirname, "..", "node_modules", ".bin", process.platform === "win32" ? "electron-builder.cmd" : "electron-builder");
execFileSync(builder, ["--linux", "AppImage", "deb", "--x64", "--publish", "never"], { stdio: "inherit", cwd: path.join(__dirname, "..") });
