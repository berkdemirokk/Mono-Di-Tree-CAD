import Foundation
import CoreGraphics
import ImageIO

/// Converts only a bounded, single-frame HEIC image to metadata-free PNG.
enum HEICPhotoConversion {
    final class Output {
        var bytes = Data()
        var exceeded = false
        func append(_ buffer: UnsafeRawPointer, count: Int) -> Int {
            guard count >= 0, count <= ExperimentalDesignBrief.maxImageBytes - bytes.count else { exceeded = true; return 0 }
            bytes.append(buffer.assumingMemoryBound(to: UInt8.self), count: count)
            return count
        }
    }
    /// Both selection paths use this after their bounded, read-only file read.
    static func prepareReference(_ original: Data, name: String) throws -> ExperimentalDesignBrief.ReferenceSelection {
        try Task.checkCancellation()
        guard !original.isEmpty, original.count <= ExperimentalDesignBrief.maxImageBytes,
              let source = CGImageSourceCreateWithData(original as CFData, [kCGImageSourceShouldCache: false] as CFDictionary),
              let type = CGImageSourceGetType(source) as String? else { throw ExperimentalDesignBrief.Error.invalidImage }
        let isHEIC = type == "public.heic" || type == "public.heif"
        let data = isHEIC ? try convert(original) : original
        let safeName = isHEIC ? "Fotoğraf.png" : name
        _ = try ExperimentalDesignBrief.make(.init(style: .describe, description: "kontrol", bust: "", waist: "", hips: "", length: "", otherMeasurements: "", image: data, imageName: safeName))
        try Task.checkCancellation()
        return .init(data: data, name: safeName)
    }
    static func convert(_ data: Data) throws -> Data {
        try Task.checkCancellation()
        guard !data.isEmpty, data.count <= ExperimentalDesignBrief.maxImageBytes,
              let source = CGImageSourceCreateWithData(data as CFData, [kCGImageSourceShouldCache: false] as CFDictionary),
              let type = CGImageSourceGetType(source) as String?, type == "public.heic" || type == "public.heif",
              let props = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
              let width = props[kCGImagePropertyPixelWidth] as? Int,
              let height = props[kCGImagePropertyPixelHeight] as? Int,
              ExperimentalDesignBrief.v2ImageGeometryAllowed(width: width, height: height, frames: CGImageSourceGetCount(source)),
              ExperimentalDesignBrief.v2ImageOrientationAllowed(props[kCGImagePropertyOrientation]) else { throw ExperimentalDesignBrief.Error.invalidImage }
        let options: [CFString: Any] = [kCGImageSourceCreateThumbnailFromImageAlways: true,
                                       kCGImageSourceCreateThumbnailWithTransform: true,
                                       kCGImageSourceThumbnailMaxPixelSize: max(width, height),
                                       kCGImageSourceShouldCacheImmediately: true]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary),
              image.width * image.height == width * height else { throw ExperimentalDesignBrief.Error.invalidImage }
        try Task.checkCancellation()
        let output = Output()
        var callbacks = CGDataConsumerCallbacks(putBytes: { info, buffer, count in
            guard let info else { return 0 }
            return Unmanaged<Output>.fromOpaque(info).takeUnretainedValue().append(buffer, count: count)
        }, releaseConsumer: nil)
        guard let consumer = CGDataConsumer(info: Unmanaged.passUnretained(output).toOpaque(), cbks: &callbacks),
              let destination = CGImageDestinationCreateWithDataConsumer(consumer, "public.png" as CFString, 1, nil) else { throw ExperimentalDesignBrief.Error.invalidImage }
        // Supplying a decoded CGImage, not the source, does not copy EXIF/GPS/XMP.
        CGImageDestinationAddImage(destination, image, nil)
        let finished = CGImageDestinationFinalize(destination)
        try Task.checkCancellation()
        guard finished, !output.exceeded, !output.bytes.isEmpty else { throw ExperimentalDesignBrief.Error.invalidImage }
        try ExperimentalDesignBrief.validateV2Image(output.bytes)
        return output.bytes
    }
}
