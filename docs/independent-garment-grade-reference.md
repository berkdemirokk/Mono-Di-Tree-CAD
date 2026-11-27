# Independent garment grading reference

The grading regression in `IndependentGarmentGradeReferenceTests` uses the published size step for Open+Pattern's **Standard Fit T-Shirt**: sizes XS–XL with 6 cm between sizes. The pattern listing identifies the DXF/PDF pattern as free under CC BY 4.0. Source: <https://openpattern.io/product/standard-fit-t-shirt/>.

The test builds synthetic front and back half-body rectangles and checks grading arithmetic: each half-panel grows 15 mm at the chest edge, and mirroring both panels across the fold yields a 60 mm whole-chest increment. The rectangles are test geometry, not copied or reconstructed Open+Pattern pattern pieces. This verifies size-step arithmetic only; it does not claim to validate the source pattern's contour, ease, fit, or factory grading.

Tolerance is 0.000001 mm because the checked edges and increments are axis-aligned whole-millimeter values; the tolerance only accommodates floating-point subtraction. The external reference publishes the size step in centimeters, converted exactly to 60 mm.

Provenance checked 2026-11-27. Open+Pattern, “Standard Fit T-Shirt,” states “Graded XS–XL (6 cm between sizes)” and “Free under CC BY 4.0.”
