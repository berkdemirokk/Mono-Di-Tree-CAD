# Notes for App Review — draft

Paste the block below into App Store Connect > App Review Information > Notes. It is in English because App Review works in English; the app's own screens are Turkish, so each Turkish button name is followed by its meaning in brackets. Button names were copied from the Swift code on branch `ios/gap-store` (16 November 2026); check them again if the interface changes before submission.

Other App Review fields:

- **Sign-in required:** No. Leave the demo account fields empty.
- **Contact information:** the owner's name, phone and email (only the owner can fill these in).
- **Attachment:** not needed. The reviewer can create a `.tcad` file inside the app (step 6) and then open it (step 7).

### Review notes (2197/4000)

```text
No account, login or internet connection is needed. The app has no in-app purchases, ads, tracking or third-party SDKs, and asks for no permissions. It works the same in Airplane Mode. Everything is stored on the device.

The interface is in Turkish. English meanings are given in brackets below.

Main flow (about 2 minutes, iPhone or iPad):

1. On the home screen tap "Örnekle dene" (Try with an example). The pattern workshop ("Kalıp atölyesi") opens with two ready-made pieces.
2. Tap the rectangle on the drawing to select it. Below the drawing, change "En" (width) or "Boy" (height) in millimetres and tap "Ölçüleri, konumu ve adı uygula" (Apply sizes, position and name). The drawing updates and the size is written on it.
3. Try the tools on the selected piece: drag it, or use the "← 10 mm" / "10 mm →" / "↑ 10 mm" / "↓ 10 mm" buttons, "90° döndür" (rotate 90°), "Kopyala" (duplicate), "Sil" (delete). "Geri al" / "İleri al" in the toolbar are Undo / Redo. Pinch or use the magnifier buttons in the corner of the drawing to zoom.
4. Optional: "Parmakla şekil çiz" (Draw a shape with your finger) opens a 300 x 300 mm sheet. Tap three or more corners, then "Şekli çizime ekle" (Add shape to drawing).
5. Scroll to "3 · Kaydet" (Save) and tap "Projeyi kaydet" (Save project).
6. Tap "Dosyayı hazırla (.tcad)" (Prepare file), then "Paylaş veya Dosyalara kaydet" (Share or save to Files) and choose "Save to Files". This is the standard system Share sheet.
7. Tap "Kapat" (Close). The saved project appears under "Son projeler" (Recent projects) on the home screen; its "..." menu offers "Adı değiştir" (Rename) and "Sil" (Delete). "Dosya aç (.tcad)" (Open file) opens the file saved in step 6 from the Files picker as a read-only preview; "Kopyasını düzenle" (Edit a copy) opens a copy in the workshop.

"Yeni proje oluştur" (Create new project) starts an empty drawing; "Dikdörtgen ekle" (Add rectangle) and "Çizgi ekle" (Add line) add shapes.

The app is a simple drawing tool for textile pattern outlines. On-screen measurements are for information only; it is not a production cutting system. Export compliance: the app uses no encryption of its own (ITSAppUsesNonExemptEncryption = NO).
```
