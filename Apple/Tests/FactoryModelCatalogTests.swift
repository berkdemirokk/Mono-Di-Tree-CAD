import XCTest
@testable import TekstilCADViewer

final class FactoryModelCatalogTests: XCTestCase {
    private func sample() -> [String: Any] {
        let model: [String: Any] = ["id":"first", "code":"2-26558", "fullName":"2-26558 (36-38-40)", "status":"unconfirmed", "sizes":[36,38,40,42], "seamAllowanceIncluded":true, "sourceIndexSha256":String(repeating:"a",count:64), "parts":[["name":"manset","group":"kumas"],["name":"manset","group":"tela"],["name":"yedek","group":"yedek"]], "notes":[]]
        var alternate=model; alternate["id"]="second"; alternate["fullName"]="2-26558 alternate"; alternate["status"]="cancelled"
        return ["format":"tekstilcad.factory-catalog", "version":1, "name":"Test", "confirmedOn":"2026-11-09", "materialAliases":["kms":"kumaş","kumas":"kumaş","kms-":"kumaş"], "cuttingGroups":["czm","yedek"], "models":[model,alternate]]
    }
    func testOwnerDeclarationsKeepVariantsRecordedSizesAndSharedParts() throws {
        let value=try FactoryModelCatalog.decode(JSONSerialization.data(withJSONObject:sample()))
        XCTAssertEqual(value.search("2-26558").count,2); XCTAssertEqual(value.search("42").count,2)
        XCTAssertEqual(value.search("alternate").first?.status,"cancelled")
        XCTAssertEqual(value.models[0].parts.count,3); XCTAssertTrue(value.models[0].seamAllowanceIncluded)
        XCTAssertEqual(value.materialAliases["kms-"],"kumaş"); XCTAssertTrue(value.cuttingGroups.contains("yedek"))
    }
    func testRejectsUnknownVersionDuplicateIdsAndInvalidState() throws {
        for field in ["version","id","status","sizes","parts"] {
            var raw=sample()
            if field=="version" { raw[field]=2 }
            else {
                var models=raw["models"] as! [[String:Any]]
                switch field {
                case "id": models[1]["id"]="first"
                case "status": models[0][field]="approved"
                case "sizes": models[0][field]=[36,36]
                default: models[0].removeValue(forKey:field)
                }
                raw["models"]=models
            }
            XCTAssertThrowsError(try FactoryModelCatalog.decode(JSONSerialization.data(withJSONObject:raw)),field)
        }
    }
    func testRejectsDuplicateKeysAndUnknownFields() throws {
        let data=try JSONSerialization.data(withJSONObject:sample(),options:.sortedKeys)
        let text=String(data:data,encoding:.utf8)!.replacingOccurrences(of:"\"version\":1",with:"\"version\":1,\"version\":1")
        XCTAssertThrowsError(try FactoryModelCatalog.decode(Data(text.utf8)))
        var raw=sample(); raw["unknown"]=true
        XCTAssertThrowsError(try FactoryModelCatalog.decode(JSONSerialization.data(withJSONObject:raw)))
    }
    #if canImport(UIKit)
    func testTwoPartModelCodesParseWithoutTreatingSizeRangesAsCodes() {
        for code in ["2-26558","1-2-3052","0-2-27511"] {
            let value=MarkerWorkOrderParser.parse("Model \(code); 36/38/40/42 beden; her bedenden 1 takım; kumaş en 80 cm; 30 saniye")
            XCTAssertTrue(value.complete,value.issues.map(\.tr).joined(separator:";")); XCTAssertEqual(value.model,code)
        }
        for code in ["36-38","2-26558-4-5","0.2-26558","x2-26558"] {
            XCTAssertFalse(ModelCodeFormat.valid(code))
            XCTAssertNil(MarkerWorkOrderParser.parse("\(code); 42 beden; her bedenden 1 takım; kumaş en 80 cm; 30 saniye").model)
        }
    }
    #endif
}
