# Friday Facts #182 - Optimizations, always more optimizations

- Источник: https://factorio.com/blog/post/fff-182
- Авторы: Rseding91
- Дата: 2017-03-17
- Темы: производительность, интерфейс
- Применимость к проекту: средняя
- Что извлечь: Перерисовка интерфейса как источник затрат: обновление ограничено изменившимися элементами, а не всем окном.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
I've done several optimizations around the game update over the past few game versions but in 0.15 I decided to also look at some of the game GUIs.
In particular there are 3 GUIs which tend to take a large amount of time when visible: the production stats, the trains view, and blueprint tooltip previews.

### GUI performance

The production stats GUI has to render an icon, a progress bar, and some text for every item in the view and that view changes each game tick as the stats do. I figured that the destruction and re-creation of all the widgets would be responsible for the slowness but as it turned out the graph was taking the majority of the time. Calculating the graphs is simple - we already store all that data to make the stats work. However rendering the graph was very poorly implemented in that every line in the graph (each up and down) was done one at a time and sent out to the graphics card. To fix that Posila batched all of the lines and send the final result out in one draw call.

Next I moved on to the trains GUI. I did some improvements to it in the middle of the 0.13 stabilization several months ago so I already knew how it functioned internally - it doesn't create and destroy anything - it reuses all the visible widgets between updates (very efficient). So, when someone commented that the game would drop to 2 FPS when they opened the window I didn't believe them. But, when I tested their save they were right. After some debugging I found that it suffered from the same problems the production stats window did: for every visible train in the GUI it would draw the minimap and all trains visible on that section of the map.

So say you have 200 trains on your map and the view is showing 50 of them: each view would render its own train + any trains in that immediate area (which ended up being roughly all of them in packed factories) and the end result was you'd get 50 * 50 trains being rendered which ends up being very slow. Cleaning up how trains are drawn on the minimap gave a nice boost to the performance here. I still have some additional ideas to improve the performance of this GUI, but it's at least manageable now for 0.15.

Finally the blueprint tooltip preview: this one stumped me for a while. I've known it was slow since I first started with Factorio 2+ years ago but could never pin-point exactly what was causing it. The drawing of the blueprint preview is near identical to what happens when you view the normal game but would always take 4-5 times as much time to render. Finally recently I found that we did zero batching of sprites to be drawn when rendering the GUI: for every sprite that we would draw it would go out to the graphics card and tell it to draw it. Fixing that was as simple as turning a flag on and now it has no measurable impact on performance when rendered.

### Weird long-standing bugs

While working on optimizations I frequently make small tweaks and re-run the game to see what difference they make. Sometimes when I make a larger change, I want to make sure nothing broke before trying it out on a larger save file, so I'll launch a new small map and test it out. While I was working on optimizing a train-heavy map earlier this week, I did just that, except I somehow corrupted the save file. I could load the save and it would crash every time i would mine a specific cargo wagon, but nothing I had changed could possibly affect the save in this way. After a few hours of testing I found a long-standing bug with trains that has hyper-specific requirements to reproduce (that I happened to only reproduce because I had a typo in an unrelated optimization I was working on):

Build 2 locomotives directly next to each other
Disconnect the 2 locomotives so they're not one train anymore
Drive the front locomotive into the rear one such that it can't move backwards any more
Drive the rear locomotive into the front one such that it can't move forward any more
Build a cargo wagon attached to the rear locomotive (making sure the cursor was more towards the rear locomotive so the cargo wagon snaps behind the back locomotive)
Try to drive the rear locomotive (with the now attached cargo wagon) forward into the front locomotive

![](https://cdn.factorio.com/assets/img/blog/fff-182-train-before.png)

In this specific setup the newly built cargo wagon will have it's connection to the rails corrupt. Any attempt to drive that train off the rails its on would crash the game. Any attempt to mine the cargo wagon would crash the game. The game still saves/loads just fine but you can't do anything with the broken cargo wagon. Finally, if you save the game and load it between steps 5 and 6 it never breaks.

![](https://cdn.factorio.com/assets/img/blog/fff-182-train-after.png)

After about an hour of debugging (and fixing an unrelated different bug) I fixed the problem by adding an "else" and 1 line of code that runs when building cargo wagons.

As always, if you have any thoughts or feedback, let us know on our [forum](https://forums.factorio.com/viewtopic.php?f=38&t=42890).
