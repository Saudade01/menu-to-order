"""Renders the sample menus used by the tests and the demo.

The menus are invented, so the repo ships no third-party photos. Each one is
rendered with headless Chrome, then degraded with Pillow to look more like a
phone photo. Run: python3 samples/render.py (needs Chrome and Pillow).
"""
import pathlib, subprocess, tempfile
from PIL import Image, ImageDraw, ImageFilter

CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
OUT = pathlib.Path(__file__).parent / "menus"

STYLE = """
body { margin:0; background:#f4ecd8; font-family: Georgia, serif; color:#2b2118; }
.page { padding:48px 64px; }
h1 { text-align:center; font-size:46px; margin:0 0 4px; letter-spacing:2px; }
.sub { text-align:center; font-style:italic; margin-bottom:28px; }
h2 { font-size:26px; border-bottom:2px solid #2b2118; margin:26px 0 10px; text-transform:uppercase; }
.row { display:flex; font-size:22px; margin:8px 0; }
.row .name { flex:1; }
.desc { font-size:16px; font-style:italic; margin:-4px 0 8px 0; color:#5a4a3a; }
"""

def row(name, price, desc=None):
    html = f'<div class="row"><span class="name">{name}</span><span class="price">{price}</span></div>'
    return html + (f'<div class="desc">{desc}</div>' if desc else "")

MENUS = {
    "kose-lokantasi": ("Köşe Lokantası", "Ev yemekleri · 1987'den beri", [
        ("Çorbalar", [row("Mercimek Çorbası", "95 ₺"), row("Ezogelin Çorbası", "95 ₺"),
                      row("Tavuk Suyu Çorbası", "110 ₺", "Şehriyeli, limonlu")]),
        ("Ana Yemekler", [row("Kuru Fasulye", "180 ₺", "Tereyağlı, pilav ile"),
                          row("Karnıyarık", "210 ₺"), row("İzmir Köfte", "240 ₺"),
                          row("Tavuk Sote", "220 ₺")]),
        ("Tatlılar", [row("Sütlaç", "90 ₺", "Fırında"), row("Kemalpaşa", "100 ₺")]),
        ("İçecekler", [row("Ayran", "35 ₺"), row("Çay", "20 ₺")]),
    ]),
    "sahil-kafe": ("Sahil Kafe", "Kahvaltı · Izgara · Tatlı", [
        ("Kahvaltı", [row("Serpme Kahvaltı (2 kişilik)", "1.250,00 TL"),
                      row("Menemen", "185,50 TL"), row("Sucuklu Yumurta", "210 TL")]),
        ("Izgara", [row("Köfte", "Yarım 180 / Tam 320"), row("Tavuk Şiş", "290"),
                    row("Günün Balığı", "Sorunuz"), row("Köfte", "330 TL", "Kaşarlı")]),
        ("Tatlı", [row("Künefe", "240 TL"), row("Sufle", "175 TL")]),
        ("Soğuk İçecek", [row("Limonata", "$4"), row("Soda", "40 TL")]),
    ]),
    "mangal-evi": ("Mangal Evi", "Odun ateşinde", [
        ("Kebaplar", [row("Adana Kebap", "380 ₺"), row("Urfa Kebap", "380 ₺"),
                      row("Beyti Sarma", "420 ₺", "Yoğurt ve domates sosu ile"),
                      row("Kuzu Şiş", "460 ₺")]),
        ("Pide & Lahmacun", [row("Lahmacun", "120 ₺"), row("Kıymalı Pide", "260 ₺"),
                             row("Kaşarlı Pide", "240 ₺")]),
        ("Mezeler", [row("Haydari", "110 ₺"), row("Acılı Ezme", "100 ₺"), row("Humus", "130 ₺")]),
    ]),
}

def html(title, sub, sections):
    body = "".join(f"<h2>{h}</h2>{''.join(rows)}" for h, rows in sections)
    return (f'<html><head><meta charset="utf-8"><style>{STYLE}</style></head>'
            f'<body><div class="page"><h1>{title}</h1><div class="sub">{sub}</div>{body}</div></body></html>')

def render(slug, title, sub, sections):
    with tempfile.TemporaryDirectory() as tmp:
        src, png = pathlib.Path(tmp) / "m.html", pathlib.Path(tmp) / "m.png"
        src.write_text(html(title, sub, sections), encoding="utf-8")
        subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                        "--window-size=900,1250", f"--screenshot={png}", src.as_uri()],
                       check=True, capture_output=True)
        return Image.open(png).convert("RGB")

def like_a_phone_photo(img, slug):
    w, h = img.size
    if slug == "mangal-evi":
        # Glare and a soft region: this is the menu the legibility signal should catch.
        glare = Image.new("L", img.size, 0)
        ImageDraw.Draw(glare).ellipse((w * 0.45, h * 0.25, w * 0.95, h * 0.45), fill=170)
        img = Image.composite(Image.new("RGB", img.size, "white"), img, glare.filter(ImageFilter.GaussianBlur(60)))
        box = (int(w * 0.5), int(h * 0.44), w, int(h * 0.54))
        img.paste(img.crop(box).filter(ImageFilter.GaussianBlur(2.4)), box)
    img = img.rotate(-0.8 if slug == "mangal-evi" else 0.6, resample=Image.BICUBIC, expand=True, fillcolor=(58, 52, 46))
    return img.filter(ImageFilter.GaussianBlur(0.6))

if __name__ == "__main__":
    OUT.mkdir(exist_ok=True)
    for slug, (title, sub, sections) in MENUS.items():
        like_a_phone_photo(render(slug, title, sub, sections), slug).save(OUT / f"{slug}.jpg", quality=82)
        print(OUT / f"{slug}.jpg")
