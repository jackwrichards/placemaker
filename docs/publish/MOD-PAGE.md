# Placemaker: the Paradox Mods page (draft)

What players would see on the mod's page. Nothing is published yet.

---

## Name

Placemaker

## Short description

Your plot's edges connect to each other and to the roads around them. Draw any area and turn it into a park, parking lot, plaza, beach or other amenity. Preview it, tweak it, build it.

## Long description

[Here's a 4-minute showcase.](https://www.youtube.com/watch?v=Fa5TWC3B-Ps)


### What you can make

- **Roads**: street grids that connect to the roads around them
- **Parking**: lots of any shape with trees, lamps, fences and marked bays
- **Parks**: parks, gardens, playgrounds, memorial parks and amphitheatres
- **Beach**: sand painted from the water's edge, quays and boardwalks
- **Shops & squares**: strip malls etc.
- **Nature**: wetlands, reserves, lookout hills, hiking trails and campgrounds
- **Farms**: farmsteads and animal parks
- **Sports**: sports parks, skate bowls and golf
- **Industrial**: container yards, car yards, junk yards and industrial works
- **Zoning**: Specify percentage breakdown of zone types and min and max lot sizes, and it auto zones for you in your selection.


### ⚠️ Early access (please read this bit)

- I consider **anarchy** essentially required to work with this mod, though it's not necessarily a requirement.

- This mod won't stop you building dumb looking stuff. I didn't want to box anyone in. So **double check your selection before you hit Build.**

Expect some bugs. Use it at your own risk.

**Back up your save before you try it.**

Built for game version **1.6.\***


### Will it wreck my save?

It wont.

- I've tested it extensively, but obviously I can't speak for every other mod compatibility.
- Still. **Make a backup.**


### Can I remove it?

Yep. Your save still loads without it.

### How to use it

1. Click the **Placemaker** button at the top left. Or hit **Ctrl+Shift+A**.

2. Select an area with **Points**, **Paint bucket** or **Paint area**. More on those below.

   You get three tools.

   - **Points**: click the corners. It snaps to curbs, buildings, right angles and road nodes.
   - **Paint bucket**: click a city block to fill it. **Shift+click** adds more.
   - **Paint area**: just brush it on. **Shift+drag** erases.
   - With any of them, **right-click** starts over (Ctrl+Z brings the area back).

   You also choose what the selection stops at. Roads, paths, water, buildings, zoning and more.

3. Under **Pick what to make**, choose a category. Then a type and a style.

   Every type has a few styles to pick from. Don't love one? Mess with it.

4. Check out the live preview. Tweak whatever you want.

   Layout, paths, planting, surfaces, the works. Then save it as your own if you want.

5. Hit **Build**. Done!


### Outgrow (beta)

- **Outgrow** settings can be found using a button at the top of the panel. It lets lots connect other to lots and roads that are nearby.

- There are many different styles of outgrow. You can mix and match any zone type with any outgrow type, and set their defaults.

- You can also paint an outgrow types on to the world with the **Outgrow Paint** button.

- It's **off by default** for new buildings.

- Want to try it? Turn it on for certain buildings. Or use the Outgrow brush on an area.

- **Careful with outgrowing every building on a huge save.** It is to iterate through each lot and the have to calculate how to connect to each other. It can take a while on larger saves, but once it's caught up, its smooth. I haven't tested this on saves beyond 1 million population.


### Found a bug?

Tell me on [GitHub](https://github.com/jackwrichards/placemaker/issues). Throw in a screenshot and your **Placemaker.log**.

It's in AppData\LocalLow\Colossal Order\Cities Skylines II\Logs. The log helps me a ton.


### Credits

Parts of Placemaker are built on these awesome mods. Go show them some love:

**Parking** has been overhauled, but some code ported from **Parking Lot Tool**, Copyright (c) 2026 KruemelGames, GPL-3.0 https://github.com/KruemelGames/ParkingLotTool

Includes **Harmony 2.2.2**, Copyright (c) 2017 Andreas Pardeike, MIT (see 0Harmony-LICENSE.txt) https://github.com/pardeike/Harmony

**Placemaker** Licensed under the GNU General Public License v3.0


Have fun :)

## Changelog (shown in its own panel)

0.2.0
- New: Outgrow (beta). Gardens and car parks grow past their lots and join up. Off by default.
- New: Zoning. Set the zone mix and lot sizes and it zones your selection.
- New: Industrial. Container yards, car yards, junk yards and industrial works.
- Fixed: white boxes after loading a save (decal packs).
- Smarter road grids and terraced blocks on slopes.
- Odd-shaped parking lots work now.
- No more lag spikes from the paint tools.
- New panel: a Placemaker | Outgrow switch at the top, scrollbars, a cleaner preview.

0.1.1: area tools: the paint bucket follows roads and buildings more exactly and previews as you hover, painting shows a live outline, slow drags now paint, and fills pick up new buildings and zoning.

0.1.0: first public version

---

## Pictures

- Thumbnail: `Properties/Thumbnail.png`: four shots (roads, parking, wetland, beach), the logo and name on a plain band.
- Screenshots: `Properties/Screenshots/`: 10 (Paradox takes at most 10): streets, street patterns (11, game runner), preview-then-build, then one per category with related types paired; each labelled with its name, how it is made and pills for its style and key settings.
