import Foundation
import CoreGraphics
import ImageIO

/// Converts a bounded, single-frame HEIC image to PNG without copying personal metadata.
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
        // Supplying a decoded CGImage does not copy source EXIF/GPS/XMP. The encoder
        // may generate benign EXIF fields for colour space and output dimensions.
        CGImageDestinationAddImage(destination, image, nil)
        let finished = CGImageDestinationFinalize(destination)
        try Task.checkCancellation()
        guard finished, !output.exceeded, !output.bytes.isEmpty else { throw ExperimentalDesignBrief.Error.invalidImage }
        try ExperimentalDesignBrief.validateV2Image(output.bytes)
        return output.bytes
    }
}

/// ImageIO can omit an invalid EXIF orientation from its decoded properties.
/// Inspect the original JPEG APP1 entries so those values cannot bypass V2 validation.
enum ReferenceImageMetadata {
    static func validateJPEGOrientation(_ data: Data) throws {
        let bytes = [UInt8](data)
        guard bytes.count >= 4, bytes[0] == 0xFF, bytes[1] == 0xD8 else {
            throw ExperimentalDesignBrief.Error.invalidImage
        }
        var offset = 2
        var inScan = false
        while offset < bytes.count {
            if inScan {
                // Entropy-coded FF bytes are escaped; restart markers have no payload.
                while offset < bytes.count {
                    if bytes[offset] != 0xFF { offset += 1; continue }
                    guard offset + 1 < bytes.count else { throw ExperimentalDesignBrief.Error.invalidImage }
                    let next = bytes[offset + 1]
                    if next == 0 || (0xD0...0xD7).contains(next) { offset += 2; continue }
                    break
                }
            }
            guard offset < bytes.count, bytes[offset] == 0xFF else { throw ExperimentalDesignBrief.Error.invalidImage }
            while offset < bytes.count, bytes[offset] == 0xFF { offset += 1 }
            guard offset < bytes.count else { throw ExperimentalDesignBrief.Error.invalidImage }
            let marker = bytes[offset]
            offset += 1
            if marker == 0xD9 { return }
            if marker == 0x01 || (0xD0...0xD7).contains(marker) { continue }
            guard marker != 0, marker != 0xD8, offset <= bytes.count - 2 else { throw ExperimentalDesignBrief.Error.invalidImage }
            let length = Int(bytes[offset]) * 256 + Int(bytes[offset + 1])
            guard length >= 2, length <= bytes.count - offset else { throw ExperimentalDesignBrief.Error.invalidImage }
            let start = offset + 2
            let end = offset + length
            if marker == 0xE1, end - start >= 6,
               Array(bytes[start..<(start + 6)]) == [69, 120, 105, 102, 0, 0] {
                try validateTIFFOrientation(Array(bytes[(start + 6)..<end]))
            }
            offset = end
            inScan = marker == 0xDA
        }
        throw ExperimentalDesignBrief.Error.invalidImage
    }

    private static func validateTIFFOrientation(_ bytes: [UInt8]) throws {
        guard bytes.count >= 8 else { throw ExperimentalDesignBrief.Error.invalidImage }
        let littleEndian: Bool
        switch Array(bytes.prefix(2)) {
        case [0x49, 0x49]: littleEndian = true
        case [0x4D, 0x4D]: littleEndian = false
        default: throw ExperimentalDesignBrief.Error.invalidImage
        }
        func integer(_ offset: Int, _ count: Int) throws -> Int {
            guard offset >= 0, count <= bytes.count, offset <= bytes.count - count else {
                throw ExperimentalDesignBrief.Error.invalidImage
            }
            var value = 0
            for index in 0..<count {
                let position = littleEndian ? offset + count - index - 1 : offset + index
                value = (value << 8) | Int(bytes[position])
            }
            return value
        }
        guard try integer(2, 2) == 42 else { throw ExperimentalDesignBrief.Error.invalidImage }
        let firstIFD = try integer(4, 4)
        if firstIFD == 0 { return }
        guard firstIFD >= 8 else { throw ExperimentalDesignBrief.Error.invalidImage }
        let count = try integer(firstIFD, 2)
        let entries = firstIFD + 2
        guard entries <= bytes.count - 4, count <= (bytes.count - entries - 4) / 12 else {
            throw ExperimentalDesignBrief.Error.invalidImage
        }
        for index in 0..<count {
            let entry = entries + index * 12
            if try integer(entry, 2) == 0x0112 {
                guard try integer(entry + 2, 2) == 3, try integer(entry + 4, 4) == 1,
                      (1...8).contains(try integer(entry + 8, 2)) else {
                    throw ExperimentalDesignBrief.Error.invalidImage
                }
            }
        }
    }
}
