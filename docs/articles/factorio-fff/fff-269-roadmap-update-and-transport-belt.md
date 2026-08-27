# Friday Facts #269 - Roadmap update & Transport belt perspective

- Источник: https://factorio.com/blog/post/fff-269
- Авторы: kovarex, V453000
- Дата: 2018-11-16
- Темы: процесс, графика
- Применимость к проекту: низкая
- Что извлечь: Смещение плана выпуска и его причины; перспектива отрисовки конвейеров.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## Roadmap update (kovarex)

A lot of people have been asking recently, when can they expect a new release and when is the game going to be finished. The original plan was to finish everything, and release the final version of Factorio ideally before the end of 2018. This was the plan at the beginning of the year. We worked in our usual way of "it is done when it is done" for quite some time, but then it started taking a little bit too long, and we weren't even sure what is a realistic timeframe to finish it in.

To help this issue, we tried to become a little more organized in the past few weeks. We went through our list of all the development tasks, and tried to finalize it. We removed all the things that we decided to cut, and added all the missing things that we need to do before the game is finished. Then we tried to make some kind of time estimate for each task, to get a general idea of when everything will be finished. We started to be more conscious of who is working on what, and how much time each task is taking, to know how accurate the estimates are. The result was, that if it all goes well, we could be done in 6-9 months. This is probably not something you wanted to hear.

After a few rounds of discussions, we decided split the releases of 0.17 and 0.18 in the following way:

**0.17 plan**

It will contain all the things we have done up to this point, mainly:

- New render backend, which helps performance and solves a lot of issues ([FFF-251](https://www.factorio.com/blog/post/fff-251))

- The graphical updates: walls, gates, turrets, belts, biters, spawners, electric poles ([FFF-268](https://www.factorio.com/blog/post/fff-268), [FFF-228](https://www.factorio.com/blog/post/fff-228), [FFF-253](https://www.factorio.com/blog/post/fff-253#defensive-structures))

- The GUI reskin ([FFF-243](https://www.factorio.com/blog/post/fff-243))

- New map editor ([FFF-252](https://www.factorio.com/blog/post/fff-252))

- Resource generation overhaul ([FFF-258](https://www.factorio.com/blog/post/fff-258))

- Robot construction tools ([FFF-255](https://www.factorio.com/blog/post/fff-255))

- Rich text ([FFF-237](https://www.factorio.com/blog/post/fff-237), [FFF-267](https://www.factorio.com/blog/post/fff-267))

- And more...

It will also include some things we know we can finish soon enough, mainly:

- Redoing some of the most important GUIs (Action bar, character screen, main game GUI, train GUI, play GUI, tooltips)

- Fluid optimisations

- And several smaller things, which depends on how it goes

We will release this during January 2019, we will announce it more precisely in advance.

**0.18 plan**

It will become the final 1.0 version once it is stable. It will contain mainly:

- New tutorial

- New campaign

- Final mini tutorials

- Revision of rest of the GUI

- All remaining high res graphics graphics and final polish

We obviously don't know exactly when is it going to be ready, but we hope it to be sooner than 9 months from now.

## Transport belt perspective (V453000)

Over time we have reworked many graphics, sometimes 'just' bringing them to high resolution, sometimes changing their design, and sometimes even changing their perspective.
Our 'camera angle' is 45 degrees, which in 'real projection' would result in rectangular tiles, but in Factorio this is contradicted by our tiles being square. This contradiction makes for a whole lot of challenges which we are addressing more and more over time. You might remember the old rails, concrete with grid, or trains which did not stretch.

Now we have found the solution for the last 'perspectively' incorrect entity - transport belts.

![](https://cdn.factorio.com/assets/img/blog/fff-269-01-old-belts.png)

The basic idea is to give the belts some kind of a wall/structure to bring them off the ground, and create the illusion that they fit into the world correctly. The transport belts have two main limitations - the belt lanes of items are not to be changed, and the belt sprites already occupy a big portion of the tile so there is very little space to show anything extra.

![](https://cdn.factorio.com/assets/img/blog/fff-269-02-old-belts-edge-space.png)

It would be a shame to abandon the idea with the conclusion of "it’s impossible", when we do physically impossible things all the time, so we thought of exactly where we can go over the tile edge to get more space.

![](https://cdn.factorio.com/assets/img/blog/fff-269-03-concept-edge-space.png)

Because of how our sprite sorting works, going past the bottom edge of the tile *should* be safe, which gives the desired effect for horizontal belts and curves. Verticals need to be happy with just a few pixels for the structure, but on the right side we can add a shadow to show their height.

![](https://cdn.factorio.com/assets/img/blog/fff-269-04-small-straight.gif)

This is the smallest unit you can build in the game and demonstrate the concept. You can now see how belts reverse in the endings and see the reversed belt running underneath.

![](https://cdn.factorio.com/assets/img/blog/fff-269-04-small-circle.gif)

We have spent many hours with Albert trying to find the final shape. How big should the holes be to show the belt movement underneath, versus how massive should the holding structure be to demonstrate the shape. In the limited space every pixel matters, and the design you see is the one we arrived at after many iterations of experiments and tests.

![](https://cdn.factorio.com/assets/img/blog/fff-269-05-piece-count.png)

Previously it didn’t matter how the endings worked, because they 'just disappeared' into the ground. Since it is now possible to see the full loop, the endings are now much more constraining for the artist, because they need to have an exact integer amount of belt pieces in order to fit into the animation.

A Normal belt tile has 4 pieces.For the ending, 3 pieces would be too long, and with only 1 piece you can’t bend the belt, so 2 pieces is the only feasible option. That means the ending is still much longer than before, which invites a bunch of glitches and problems too, but most of them had *easy* solutions, or our sprite sorting 'just handled it'.

This seems to work more or less until you see and realize that belt sprites are being flipped by the engine, which causes the shadows to break entirely, while the horizontal and curve lighting isn’t having a great time either. In the following picture you can see that each sprite has 1 correct, and 1 wrong use, but it’s the same sprite so you can’t fix the issue  just by changing the sprite without breaking the other use.

![](https://cdn.factorio.com/assets/img/blog/fff-269-06-flipping-problems.png)

Therefore we are finally getting rid of all flipping logic for belt sprites! Originally the flipping logic was there to save as much VRAM as possible because belts used to be a significant portion of all VRAM. Nowadays they are not, and it makes little sense to save memory at all costs on one of the most visible entities in the game.

I already found the flipping super weird when I was working on the high resolution belts ([FFF-154](https://www.factorio.com/blog/post/fff-154)), mainly the fact that 3 of the defined curves would almost make a circle, but the fourth would be mirrored for 'reasons'. Now each 'rotation' has its own unique sprites, which allows us (and mods) to be a lot more creative with custom belt sprites, and it is much easier to work with.

More problems were caused by exceeding the bottom and right edges of the tile. From some pieces drawing incorrectly over other pieces, through the shadows showing where they shouldn’t, to belt endings and underground belts breaking badly in multiple cases.

We were speaking about redesigning the underground belt structures with Albert multiple times already, and this was an ideal opportunity, but a redesign alone couldn’t fix everything...

![](https://cdn.factorio.com/assets/img/blog/fff-269-08-broken-undergrounds.png)

The underground belt structures are always drawn above belts and icons - that means if they go below the tile border, they draw over belts in the front tile which should not happen. You can see neither of the previous problems are present in the following screenshot:

![](https://cdn.factorio.com/assets/img/blog/fff-269-07-fixed-undergrounds.png)

Mods can now define back_patch and front_patch layers to underground belt structures. The front patch prevents the structure from overlapping with the belts in front of it. The back patch prevents the structure from overlapping with the items inside the structure.

![](https://cdn.factorio.com/assets/img/blog/fff-269-09-underground-structure-layers.png)

The new underground belt structure now fits better to the rounded shape of the endings and covers everything it needs to cover, but in some cases it also reveals more than before!...

![](https://cdn.factorio.com/assets/img/blog/fff-269-10-underground-side-loading.gif)

As a bonus, the underground belt structure has a variant which shows a hole when you side-load into the underground belt.

![](https://cdn.factorio.com/assets/img/blog/fff-269-belt-swipe.gif)

It’s been a lot of work of iterating, fixing glitches, and solving problems, but you can see that the correct perspective makes the whole picture feel much more natural than before. We believe this will have a very positive impact on your experience in 0.17.

As always, let us know what you think on our [forum](https://forums.factorio.com/63491).
