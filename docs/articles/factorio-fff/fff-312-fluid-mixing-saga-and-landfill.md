# Friday Facts #312 - Fluid mixing saga & Landfill terrain

- Источник: https://factorio.com/blog/post/fff-312
- Авторы: Dominik, Ernestas, Albert
- Дата: 2019-09-13
- Темы: симуляция, геймдизайн
- Применимость к проекту: средняя
- Что извлечь: Запрет смешивания жидкостей выглядел простым правилом, но потребовал разбора всех способов соединить трубы; пример правила, стоимость которого выяснилась при реализации.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## Fluid mixing saga Dominik

Hello Factorians,

Today I would like to talk to you about my favourite subject - fluid mixing and its prevention. It is a new mechanic introduced in 0.17 that seemed quite simple at first, but has been giving me nightmares ever since.

A while ago I took on the task of updating the fluid system ([FFF-260](https://factorio.com/blog/post/fff-260)). The way it works in 0.16 is that the fluid boxes (the thing that holds the fluid and is contained in entities like pipes or refineries) had no organisation whatsoever except their connections. They would sit there and do nothing and then once per update send fluid somewhere. It had it's issues especially with symmetry, and when you happened to put some fluid where it did not belong, you could end up requiring major demolition works.

My task was to develop a better algorithm to move the fluids, and a very very optional side task I had in mind was to do something about the fluids getting to wrong places and mixing. We started by organizing the fluid boxes into systems (connected FBs make a system) managed by a special fluid manager, and optimizing the heck out of it ([FFF-271](https://factorio.com/blog/post/fff-271)).

Soon after, I started working on the new algorithm and anti fluid-mixing. Until then nobody really considered preventing the fluid mixing seriously as it did not seem possible. But when I thought about it, it did not seem that hard. The idea is to simply not allow any action that would lead to fluids getting where they are not supposed to go. When you build a steam pipeline, you should not need nor want it to touch another fluid.

In principle, it would only require a few quite realistic mechanisms:

A connected block of fluid boxes would either be fluid-free, or it would be locked to one fluid.
Two such blocks can connect only if they are compatible - either one has no fluid lock, or they have the same fluid.
An assembler can only set a recipe if it is compatible with the fluid locks on its fluid boxes.
Have a migration from old saves that can contain mixing.

By creating the fluid systems right before, number 1 was almost finished and we only needed to assign the lock. It would be set either by a fluid, or by a 'filter', which is a fluidbox that is set to use a fluid - such as a water pump using water, or assembler having some inputs/outputs given by a recipe.

After a while I had both the fluid algorithm and the mixing done ([FFF-274](https://factorio.com/blog/post/fff-274)). The mixing was not that easy (like 5x more complicated) but it worked pretty well. As for the fluid algorithm, V453000 and Twinsen found some issues with waves on a macro scale, and because it was right before releasing  the 0.17 experimental, we decided to hold it off on it for the time being (we have a new version now that seems okay, but has to wait for 0.17 becoming stable first). The mixing made it through though and seemed quite finished.

It turned out that the work on it was at most one tenth complete.

![](https://cdn.factorio.com/assets/img/blog/fff-312-fluid-bugs.png)

Some difficulties appeared already before the initial release, but that was only a hint of what would come later. One by one, then ten by ten, bugs started coming. The problem is that as often as not, they were not just little issues with simple fixes. Instead, over and over, they turned the current solution on its head and the code had to be completely rewritten in order to account for the new case. As of now, almost exactly half a year and many rewrites later, it is still not fully bug free.

The number one issue was with Assembling machines. It turned out that their code was pretty messy and does not simply allow the checks I needed, so it had to be refactored. The next problem was that the check was not only required when setting a recipe on an existing assembler - as I naively thought - but in about one million different situations I would not dream existed. Building a fixed recipe assembler. Reviving an assembler. Rotating. Blueprint of an assembler. Blueprint with a recipe and a rotation placed over a ghost with a non-default rotation during a full moon. Teleport. Script building. Copy-paste settings. Any combination of these.

Pretty much every case would go through different paths in the code and behave entirely differently. Fixing one would break another and so tons of tests had to be written. When these cases finally worked, it turned out that doing these operations when they fail (e.g. can’t rotate because it would cause mixing) brings another wave of issues. And then mods come and the complexity multiplies. All this, although in a more simple form, had to be done for all other entities that can use fluid too, such as inserters (mods...).

Another number one issue are underground connections. When playing, you would not think twice about them but on a closer look they are all but simple. They have this (cough) uncomfortable feature (want to shoot myself) that you can build another underground pipe between them (or take it out) and a complex reconnection logic jumps in. It means that based on building order, connections are established and reestablished differently. This is a big thing when building a blueprint with many pipes, or loading a save game.

But the largest issue is one tiny corner case with huge implications. Have two underground pipes that have different fluids, but are split by a third, and it gets removed. Until now mixing was theoretically completely avoidable, but here it was and there was nothing to stop it.

![](https://cdn.factorio.com/assets/img/blog/fff-312-underground-pipe.png)

As a result, a new concept is introduced - a blocked connection. An underground connection that would normally connect but can’t. Establishing it is the easier part. But when does it get fixed? An entity miles away gets rotated, that splits a fluid system, disconnecting the blocked connection from a fluid filter on the other side, and the connection should unblock. But even something as simple as a rotation contains fluid fixes and re-establishing of fluid boxes and if it fails, it still fixes the blocked connection in the process and it can’t be undone... You get the picture. And we are still talking about underground pipes, while anything can have underground connections, including said assemblers, which the previous code did not consider at all. The complexity just goes deeper and deeper, and Rseding is probably right that we should have never gone this way at all.

So the bug fixing has been basically running in circles - clearing one batch of bugs, thinking that those were the last ones and the ordeal is finally over, only to find another batch (ideally from some bizzare modder idea) a few days later. Many times I wished that mods never existed. Nor multiplayer, or any players at all for that matter.
About two months ago all seemed really fine, with basically no reports and just a few crashes in the automated crash reports.

At that moment another nightmare materialized in the form of a talented volunteer bug tester named boskid, who took on a personal crusade to break the fluids to dust (I actually imagine him sitting in his dark room with evil laughter, dreaming about making my next day worse than the previous). In all seriousness, he has done a great deal of work with his bug testing, creating bizzare modded cases and testing scripts, and deserves a big thanks. He is actually coming to our offices next week, so follow the news for reports of developers falling out of windows.

![](https://cdn.factorio.com/assets/img/blog/fff-312-fluid-mixing.gif)

An example of a nice configuration he found (and I can’t fix).

The whole time we were taking the offensive approach to mixing - crash the game when it happens - so that we know about bugs to fix them. Recently we have decided to put a stop to it and have the mixing automatically fix itself once it is detected, eyeing the end of this episode. Right now I have 3 mixing bugs on the list and I am sure those are the last, and mixing will be done (lying to myself is a way to cope with it) and the new fluid algorithm can come soon after :).

![](https://cdn.factorio.com/assets/img/blog/fff-312-pvp.gif)

(And of course, boskid found a way to use the automatic mixing fixing as an evil PVP tactic.)

## Landfill terrain Ernestas, Albert

This week Ernestas came physically to the Wube office to spend some time with us (he is normally working remotely), so we are taking the chance to finish things that we had in the drawer that needed closer communication. One of those things is a specific terrain for landfill. As you know we were using grass for it. Not anymore.

![](https://cdn.factorio.com/assets/img/blog/fff-312-landfill-terrain.png)

In Factorio we have a clash of two worlds:

The natural and organic planet, off-grid with multiple terrain types, trees and doodads.
The world of the factory, which is mathematical, regular, modular, and totally attached to the grid.

This new terrain represents a boundary in between those two worlds. The grid on its surface tries to express the artificiality of it, like mechanically pressed with some sort of industrial stamp. The imperfections of the grid and its irregularities are holding this man made terrain into the realm of the natural environment.

For now the sound of the landfill action is not solved, 'soon' we’ll take care of it.

As always, let us know what you think on our [forum](https://forums.factorio.com/75657).

                [Discuss on our forums](https://forums.factorio.com/75657)

                [Discuss on Reddit](https://www.reddit.com/r/factorio/comments/d3nx9t/friday_facts_312_fluid_mixing_saga_landfill/)
