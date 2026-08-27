# Friday Facts #179 - New resource graphics & concrete

- Источник: https://factorio.com/blog/post/fff-179
- Авторы: V453000
- Дата: 2017-02-24
- Темы: графика, конвейер ассетов, генерация мира
- Применимость к проекту: средняя
- Что извлечь: Процедурная сборка спрайтов руды скриптом в Blender: случайные повороты, смещения и материалы дают вариативность без ручной работы; мозаичная форма тайла скрывает сетку.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
While all us minions and assembling humans are furiously working on 0.15, today I would like to present to you what I have been working on lately - new graphics for resources, of course including high res and the new uranium ore. In the second part of the article I will get to an old/new topic about concrete.

### New resources - the idea

For 0.15, we needed uranium ore, and eventually needed all the other resources in high resolution, so we took the opportunity to rework all of them now (not oil).

Most importantly, we introduce a new way of tiling - instead of a square, a resource tile is a puzzle-shaped thing. This helps to remove the top-down square nature of the old sprites, and make the ore patches much more organic.

![](https://cdn.factorio.com/assets/img/blog/fff-179-1-tiling-patch.png)

### New resources - the process

![](https://cdn.factorio.com/assets/img/blog/fff-179-2-copper.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-179-2-iron.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-179-2-coal.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-179-2-stone.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-179-2-uranium.gif)

Since the aim is to create multiple tilesets which work similarly, I tried to save a lot of time by making one master scene (I started with copper ore), from which it would be easy to derive the rest of the ores.

![](https://cdn.factorio.com/assets/img/blog/fff-179-3-copper-others.png)

Of course the most basic way to do this would be to just recolour them in photoshop and call it a day (which would be pretty lame if you ask me), but if we are already redoing it, we might as well create a robust system which can actually change the 3D objects in a semi-automatic way.

Once again, I embraced the power of python and made a script which randomizes the selected objects. More specifically, it tweaks some displacement values, changes the mesh itself, and randomly rotates+scales it in the Z axis. The material itself has some further randomization inside for even more variety.

![](https://cdn.factorio.com/assets/img/blog/fff-179-4-script.gif)

Making the first one took a while, most of the time I spent finetuning the amount of volumes in the density levels, and making sure they tile nicely together. After that work on the remaining ores was very fast.

Still working on the uranium though, it’s very different (we want it to be very different) so it takes a lot of figuring out how it should actually look like.

Please note that realism isn’t the aim of this, the main focus is to have something that intuitively looks like what it should, for example copper ore looks closer to copper plate than to real copper ore.

![](https://cdn.factorio.com/assets/img/blog/fff-179-5-ores.png)

With this we are also currently tweaking the map generation, so that the ore patches look nicer. They will have the same amount of resources on average so it won’t really affect gameplay.

### Concrete

Quite some time ago we [presented](https://www.factorio.com/blog/post/fff-120) a new concrete tileset, way back before 0.13 was released. Although it’s much nicer than what we had previously, it introduced a new problem with the hazard concrete (concrete with stripes). You can see the problem on the left of the following picture.

![](https://cdn.factorio.com/assets/img/blog/fff-179-6-concrete.png)

Recently Posila spent just a few hours implementing a new lua value - transition_merges_with_tile = "concrete" - which just uses a continuous border between the two tile types instead of causing the ugly behaviour. It’s still not a perfect solution because the edge doesn’t know whether it should be hazard or normal concrete, so it just searches for its neighbours in a north-east-south-west cycle - so sometimes you can get a different tile than you might expect. We believe it’s much nicer than what we had so far, and in most cases it pretty much works.

One day we will have to rework the concrete to high-res anyway, so hopefully we will be able to figure out a better system by then. For now, you can look forward to it in 0.15.

As always, we can’t wait to read your reactions and feedback on our [forums](https://forums.factorio.com/viewtopic.php?f=38&t=41815).
