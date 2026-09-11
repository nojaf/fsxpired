# Assets

`logo.png` is the original, full-size logo. The copies the site and the package use are derived
from it and should be regenerated from it, never edited by hand:

```shell
magick assets/logo.png -filter Lanczos -resize 120x120 -strip docs/img/logo.png
magick assets/logo.png -filter Lanczos -resize 128x128 -strip assets/icon.png
magick assets/logo.png -filter Lanczos -resize 64x64 -strip docs/img/favicon.png
```

- `docs/img/logo.png`: the header logo, twice the size it is shown at, for high density screens.
- `assets/icon.png`: the NuGet package icon, the 128px NuGet recommends.
- `docs/img/favicon.png`: the favicon.
