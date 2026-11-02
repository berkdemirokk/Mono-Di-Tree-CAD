// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "ModelStorePrototype",
    platforms: [.iOS(.v16), .macOS(.v13)],
    products: [.library(name: "ModelStorePrototype", targets: ["ModelStorePrototype"])],
    targets: [
        .systemLibrary(name: "CSQLite"),
        .target(name: "ModelStorePrototype", dependencies: ["CSQLite"]),
        .testTarget(name: "ModelStorePrototypeTests", dependencies: ["ModelStorePrototype", "CSQLite"])
    ]
)
