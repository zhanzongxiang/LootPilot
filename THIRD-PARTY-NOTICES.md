# Third-party notices

LootPilot includes or depends on the following third-party software and model assets. Their licenses apply to those components independently of LootPilot's MIT license.

## RapidOcrNet 3.0.0

- Project/package: <https://www.nuget.org/packages/RapidOcrNet/3.0.0>
- License: Apache License 2.0
- Use: .NET OCR inference and its transitive runtime dependencies

## PaddleOCR / PP-OCR models

- Project: <https://github.com/PaddlePaddle/PaddleOCR>
- License: Apache License 2.0
- Included assets: PP-OCRv6 detection and recognition ONNX models, direction classifier model, and recognition dictionary under `src/TarkovPriceOverlay/Assets/OcrModels/`
- Model files may have been converted to ONNX for local inference; the original project attribution and license remain applicable.

The Apache License 2.0 text is available at <https://www.apache.org/licenses/LICENSE-2.0>.

## External data services

LootPilot can request public item-price data from `api.eftarkov.com` and `api.tarkov.dev`. These services are not bundled with, operated by, or guaranteed by LootPilot. Their availability, data and terms remain controlled by their respective operators.
