# The gazetteer

`src/PhotoGallery.Infrastructure/Places/cities500.br` is the data behind place
names. It is compiled into the executable, so place names work on a library's
first run with an empty working folder and no network — nothing to download,
nothing to install.

## What it is

GeoNames' `cities500` export: every populated place with 500 inhabitants or
more. **235,403 rows**, of which 740 are in Malaysia.

| | |
|---|---|
| Source | https://download.geonames.org/export/dump/cities500.zip |
| Snapshot | 16 August 2026 |
| `cities500.txt` | 40,723,538 bytes |
| SHA-256 | `070888569144fe957e5edd5250995daf6099a411f8c26969e46cb05f48ffec7a` |
| Embedded here | 2,960,081 bytes |

## Why it is a thirteenth the size

GeoNames ships nineteen columns and this app reads six. The bulk of the original
is `alternatenames` — every local spelling of every place, in every script —
which nothing here ever looks at. Dropping it and the twelve other unused
columns takes 38.8 MB to 9.3 MB; Brotli takes that to 2.8 MB.

Coordinates are rounded to four decimals, about 11 m. The search refuses
anything beyond 30 km, so a further eleven metres of precision would be storage
spent on a distinction nothing can act on.

The layout is six tab-separated columns, one place per line:

```
3038832	Vila	42.5318	1.5665	AD	03
id      name    lat     lon     country admin1
```

`id` is GeoNames' own identifier, kept because it is stable across their
releases — a row number would shift under a later dump and leave stored places
pointing at other towns.

## Remaking it

Download and unzip `cities500.zip`, then:

```bash
awk -F'\t' '{printf "%s\t%s\t%.4f\t%.4f\t%s\t%s\n", $1,$2,$5,$6,$9,$11}' \
    cities500.txt > cities500.tsv

brotli -q 11 -f cities500.tsv -o cities500.br
```

Check the result before committing it: `brotli -d -c cities500.br | wc -l`
should report the same row count as the source, and
`GeoNamesGazetteerTests.TheEmbeddedGazetteer_NamesSomewhereKnown` will fail if
the file did not embed or does not parse.

Updating the gazetteer is therefore a code change rather than a drop-in. That is
the price of needing no install step, and it is paid rarely.

## The region table

`src/PhotoGallery.Infrastructure/Places/admin1.br` names the first-level
divisions — "MY.06" is Pahang, "AU.07" is Victoria. The gazetteer stores only the
code, so without this a region can be filtered on but never shown or typed.

| | |
|---|---|
| Source | https://download.geonames.org/export/dump/admin1CodesASCII.txt |
| Snapshot | 17 August 2026 |
| Downloaded | 151,536 bytes, 3,865 rows |
| SHA-256 | `590651498043f674accda2b7f46d21286cda0e290b02f8561c5005eee9a5448c` |
| Embedded here | 27,041 bytes |

Two tab-separated columns, `code` and `name`, from the source's first two:

```bash
cut -f1,2 admin1CodesASCII.txt > admin1.tsv
brotli -q 11 -f admin1.tsv -o admin1.br
```

Not every country has one. Singapore and Hong Kong are city-states with no
divisions to name, so a lookup there answers null and the screens show nothing
between the district and the country — which is how an address for those places
reads anyway.

Countries are **not** a file. They are 246 lines in
`Places/CountryNames.cs`, because that is the most stable reference data there
is and a table that small is better read in source than unpacked at runtime.
`CountryNamesTests` holds it to the gazetteer in both directions, so a code in
the data with no name fails the test run rather than putting "HK" in front of
somebody.

## Licence

GeoNames is licensed **CC BY 4.0**, which requires attribution. Using the data
would have required it; shipping it inside the executable makes this application
a redistributor, so the credit is not optional and is shown in About.

> Place names from GeoNames — https://www.geonames.org — CC BY 4.0

## What it cannot do

`cities500` knows populated places, not landmarks. Genting Highlands is a resort
rather than a town, so the coordinates there resolve to **Kampung Bukit Tinggi,
Bentong, 9.1 km away** — accurate, and not the word anyone would use. A fuller
GeoNames dump (`allCountries`, ~380 MB) carries landmarks and would name it
exactly; the format above does not preclude moving to a trimmed subset of one
later.
