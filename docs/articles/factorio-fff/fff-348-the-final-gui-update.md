# Friday Facts #348 - The final GUI update

- Источник: https://factorio.com/blog/post/fff-348
- Авторы: V453000, Twinsen, Ian, Klonan
- Дата: 2020-05-22
- Темы: интерфейс, графика, звук
- Применимость к проекту: средняя
- Что извлечь: Завершение переработки интерфейса: иконки высокого разрешения, обновление стилей, звуковое сопровождение действий.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
It's been [over 4 years since we planned the infamous GUI update](https://cdn.factorio.com/assets/img/blog/fff-348-postpone.PNG). If all goes well, next week the game will get the last big GUI update for 1.0. While the state of the GUI is not close to our crazy plans we recently had for the GUI, it's above what we initially planned 4 years ago.

The update you will see next week includes:

- A visual update to over 100 game GUIs

- New high resolution icons for all game items (visible both in GUI and in the world)

- New GUI sounds for most interactions

## High resolution icons V453000

### The plans

A bit more than a year ago Albert had posted twice ([FFF-290](https://www.factorio.com/blog/post/fff-290) and [FFF-291](https://www.factorio.com/blog/post/fff-291)) about our thoughts, experiments, and plans of creating high resolution icons.

Factorio has a ridiculous amount of items and things that need icons, which by itself means it’s a lot of work to redo them. I did not work on the new icons non-stop for a year as there were other more pressing tasks to be done sometimes, but they still took a large amount of time to make.

As Albert mentioned earlier, we also wanted to create multiple mipmaps per icon to control scaling better than letting the engine rescale icons by crazy amounts, but it could multiply the amount of required images to astronomic numbers...

### The process

Since we were very well aware of the amount of work that the icons would require, we tried to set up an efficient workflow first, and it paid off big time.

![](https://cdn.factorio.com/assets/img/blog/fff-348-icons-blender-64.png)

A rather simple Blender startup scene to standardize output paths and basic starting settings was a good start, but the part that saved my sanity was a python script which automatically creates mipmaps from given input images, and downscales images from nearest available higher resolution if an input is missing. And automatically puts the mipmap into the game, assuming the file names are correct.

![](https://cdn.factorio.com/assets/img/blog/fff-348-python-64.png)

This is huge because it means I could create the highest resolution image first and immediately put it in the game, and see if conceptually the icon is working.
Once I would consider the highest resolution good enough, I could just downscale each of the resolutions manually in Photoshop and paint the extra necessary edits they would require.

![](https://cdn.factorio.com/assets/img/blog/fff-348-rail-icon.png)

The best part is that the mipmaps were not really extra work, they were actually a lifesaver. Creating a high resolution icon that is trying to be readable, look good, represent what it should and do all of it in any scaling level, can make iteration really difficult, as by changing a pixel in the image can introduce problems in completely unknown zoom levels.

It feels rewarding for "trying to do things right" when a solution that we initially thought is more proper looking but potentially more work, is actually more proper looking and less work in the end.

![](https://cdn.factorio.com/assets/img/blog/fff-348-fish-64.png)

### The result

![](https://cdn.factorio.com/assets/img/blog/fff-348-icons-144.png)

Some icons have been around for years and the entities they represented have changed, but the icons have not. Because of this and many other reasons, some icons will take some getting used to as they look different now. As a benefit of working on the icons on and off for more than a year, I also got to play the game with them over the year and I have gotten used to them already.

[![](https://cdn.factorio.com/assets/img/blog/fff-348-icons-1-896.png)

Click to view full resolution](https://cdn.factorio.com/assets/img/blog/fff-348-icons-1-fullsize.png)

Since we have updated our GUI to draw in high resolution with GUI scales like 200%, the item icons suddenly have too low resolution, so we needed to update the icons. That’s what I was thinking at first, but as I started seeing high resolution icons in the game (on belts, assembling machines, cargo wagons, in the map markers, and everywhere else...) I realized how much impact the new icons actually have.

Please do try to get used to the more revolutionary icons first, but do let us know about problematic cases - reworking all icons is a gargantuan project, but redoing individual icons can be very quick and done silently without breaking any mods. That’s what we’ve been doing in almost every major release after all.

​

## The GUI style update Twinsen

As mentioned in [FFF-338](https://www.factorio.com/blog/post/fff-338), the plan was to finalize the transition of styles, fix obvious issues and low hanging fruits, and try to get everything at a consistent level of quality for 1.0. While it didn't sound like much, it was quite a massive undertaking. Factorio has 800 styles defined in Lua and 400 widget types out of which over 100 are proper in-game windows. Going though each of them, looking for issues, finding the responsible code, understanding it and changing it takes time, especially when doing this while trying to account for all the possible situations, such as mods doing crazy things. After doing it 100 times you start questioning your sanity.

In game you will notice that some layouts (such as logistic containers) were improved, broken layouts (such as the car/tank inventory) were fixed, plus about a hundred small things like better tilesets, better margins and paddings, proper centering of some elements, etc. Here are some examples of what you will see.

![](https://cdn.factorio.com/assets/img/blog/fff-348-new-gui-styles.png)

About the fact that we took 4 years, it's partly because (as is probably quite common in game dev) we underestimated the complexity of the GUI. Add on top of that some disorganisation and a constantly expanding and changing scope and you get a project that never finishes. So finally I decided to put my foot down, call it good enough and say no to the constant stream of changes.

That means it's not all good news. It doesn't look like the Blueprint Library will be getting an update before 1.0, currently making it a very low hanging fruit as far as the GUI is concerned.

Such a big update comes with some mod breaking changes, mostly related to changed Lua styles. In order to help modders in fixing the mods as soon as possible and also improving their styles to make them look closer to the base game, I made a long post in the [upcoming breaking mod changes topic](https://forums.factorio.com/viewtopic.php?p=495184#p495184). We initially intended to remove the old "gui.png" file, but in the end decided to keep it in case mods want to use parts of it. Even if some old styles were removed, mods can still reference the tileset there.

After it's released, let us know what you think in the release post, or report any bugs you find on the [bug forum](https://forums.factorio.com/viewforum.php?f=7).

​

## Equipment grid improvements Twinsen

In the meantime, Ondra worked on some improvements to the equipment grid. As part of the icon update, this also includes some new graphics for the equipment sprites themselves.

![](https://cdn.factorio.com/assets/img/blog/fff-348-equipment-grid.png)

## UI sound effects Ian

As I mentioned in a recent post ([FFF-341](https://www.factorio.com/blog/post/fff-341)), I have been mostly working on UI sounds. My approach has been to get across the tactile feel of the buttons, plus the appropriate feedback for a particular action. But sometimes you don't really know until the rest of the team have heard them.

[видео: fff-348-gui-sounds.mp4](https://cdn.factorio.com/assets/img/blog/fff-348-gui-sounds.mp4)

After prototyping ghost building for example, I thought I had a set of sounds that worked, however Vaclav gave me the valuable feedback that they were too much like 'in world' SFX and needed to be more electronic. This makes sense, as building with ghosts is really sending an instruction to the robots. We also have sounds for rotating entities, which is something you asked for. It provides a bit of useful and fun feedback which could be helpful if you hit the 'R' key by mistake when you meant to open your inventory.

[видео: fff-348-ui-sounds.mp4](https://cdn.factorio.com/assets/img/blog/fff-348-ui-sounds.mp4)

In the case of night vision equipment, I started by making a subtle whoosh type sound but Klonan suggested something more like the 'wind up' sound in Splinter Cell, so I made a kind of a homage to that using a mixture of a rising synth sound and an electrical contact effect.

[видео: fff-348-nightvision.mp4](https://cdn.factorio.com/assets/img/blog/fff-348-nightvision.mp4)

I asked Posila if we could play a different building sound for landfill. Once this was done I spent a whole day creating and play testing a variety of sounds, being careful to balance the levels between the impacts and the subtle water splash behind it. I felt they work so well that when Vaclav asked me to look into changing the concrete tile sounds, I used a similar approach and that mechanic also feels much more satisfying now.

[видео: fff-348-landfill.mp4](https://cdn.factorio.com/assets/img/blog/fff-348-landfill.mp4)

Val has been working on more variety for the different sizes of enemies, plus some new sounds for walking on rails. From now on it will be mostly final mix adjustments, including final music mastering and making the whole soundtrack uniformly a bit louder.

## G2A resolution Klonan

Back in [FFF-303](https://factorio.com/blog/post/fff-303), we talked about our thoughts on the grey market websites and more specifically about G2As vow to pay back 10x the money lost to chargebacks.

Well its been a long time, but we're happy to say we have reached the conclusion of the story. You can read the press release from G2A [here](https://www.g2a.com/news/latest/g2a-vows-to-pay-devs-10x-the-money-proven-to-be-lost-on-chargebacks/) and the interview with GamesIndustry.biz [here](https://www.gamesindustry.biz/articles/2020-05-20-g2a-and-wube-software-settle-usd40-000-chargeback-dispute).

In short, G2A has confirmed that 198 of the ~300 Steam keys we had recorded from fraudulent purchases were sold on the G2A platform, and they have kept their promise and have sent us 10x the chargeback fees (which was roundabout $20 an order).

That is pretty much it. G2A were quite open during the discussions, and we don't doubt the results they have provided. We still don't recommend purchasing Factorio from any unofficial sources, and there is no ongoing relationship or agreement with G2A after this.

I'd like to thank the team members at G2A who put in the effort to try to close this topic, even though there are more pressing concerns at the moment for all of us.

                [Discuss on our forums](https://forums.factorio.com/85180)

                [Discuss on Reddit](https://www.reddit.com/r/factorio/comments/gom295/friday_facts_348_the_final_gui_update/)
