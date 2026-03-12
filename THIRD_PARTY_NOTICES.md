# Third-Party Notices

This project includes and/or depends on third-party components. Their licenses apply to those components.

## pdf.js

- Component: PDF viewer (pdf.js)
- License: Apache License 2.0
- License file: `DevotionDesk/PdfViewer/pdfjs/LICENSE`
- Notes: The pdf.js distribution also includes license files for bundled font assets:
  - `DevotionDesk/PdfViewer/pdfjs/web/standard_fonts/LICENSE_FOXIT`
  - `DevotionDesk/PdfViewer/pdfjs/web/standard_fonts/LICENSE_LIBERATION`
  - `DevotionDesk/PdfViewer/pdfjs/web/cmaps/LICENSE`

## Montserrat Font

- Component: Montserrat (font)
- License: SIL Open Font License 1.1
- License file: `DevotionDesk/Fonts/Montserrat-OFL.txt`
- Source: https://github.com/google/fonts/tree/main/ofl/montserrat

## SharpVectors

- Component: SVG rendering (WPF)
- Package: `SharpVectors.Wpf`
- License: BSD 3-Clause
- License text: `licenses/SharpVectors-License.txt`
- Notes: Used to display the bundled SVG logo in the custom title bar.

## Microsoft WebView2

- Component: WebView2 (NuGet package used by the app)
- Package: `Microsoft.Web.WebView2`
- Notes: WebView2 Runtime is required on the machine to run the embedded browser control.

## Bible Data / Services

- Online API (when used): https://bible-api.com/
- Notes: The service is rate-limited and may be unavailable at times; offline downloads use sources referenced in the app.
