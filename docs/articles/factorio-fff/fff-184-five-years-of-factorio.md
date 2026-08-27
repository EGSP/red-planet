# Friday Facts #184 - Five years of Factorio

- Источник: https://factorio.com/blog/post/fff-184
- Авторы: kovarex, Rseding
- Дата: 2017-03-31
- Темы: ввод, производительность, процесс
- Применимость к проекту: средняя
- Что извлечь: Система разрешений на действия ввода: действие игрока представлено данными, которые можно проверять и ограничивать до применения. Отдельно — ускорение строительства железной дороги.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
![](https://cdn.factorio.com/assets/img/blog/fff-184-cake.png)

Today, it is exactly 5 years since the initial Factorio commit. As you may, or may not know, the first version was created in java, and it took me (kovarex) a whole 12 days to realize that it is not a good idea, and I switched to C++. As a small celebration I provide the [Factorio pre-alpha version 0.1](https://cdn.factorio.com/assets/misc/Factorio-prealpha9-campaigns1RE.zip). It is a good reminder of how much the game has moved forward in all directions. The controls are cumbersome, the graphics are funny and glitchy, the GUI is horrible. The campaign has 4 levels, where the last one is quite a challenging defense mission. There are also 2 savegames with one of the first Factorio Factories ever created.

![](https://cdn.factorio.com/assets/img/blog/fff-184-early-factory.png)

### The input action permissions system *(Rseding)*

The improvements to how multiplayer works in 0.14 made having an open multiplayer game not out of the question. However this introduced a new problem, in that there is no way to protect yourself from people that just want to ruin the fun. To try to fix those problems I've added 3 new things for 0.15: white-list support, the ability to disable friendly fire, and an input action permissions system.

Factorio handles all player actions and interactions through our system of 'input actions'. The input actions tell the game what the player is trying to do, where and what keys/clicks he is making, and basically all game state interactions are done with input actions. This makes it relatively easy to filter what players can and can't do, by limiting what input actions they can perform, which can now be controlled through the permissions system:

![](https://cdn.factorio.com/assets/img/blog/fff-184-permissions-2.png)

The permissions system is completely optional, and it supports any number of permission groups, allowing server admins to define allowed/disallowed actions for everyone in that group. Before anybody asks: yes you can deny yourself the ability to edit permissions (after confirmation), if for some reason you wanted to do that.

The server console (and RCON) have unrestricted access to edit permissions. So, if you do lock yourself out (I don't recommend you do), you can always get back in through that. Additionally, permissions can be edited through the Lua API since the next logical step would be some automated system to slowly grant players permissions on a public server. The ability to edit permissions through the Lua API is checked so a player with console access can't just give themselves permissions if they don't have the required permissions.

### Railway building optimizations *(Rseding)*

Every so often someone would make a save file built almost exclusively around trains, and run into the same performance problem: when you've got enough trains going, the building and destruction of rail related entities starts to consume so much time that it borders on unplayable. I've known about the performance problems for a while now, but the rail system was one of the few areas I didn't have experience with (knowing how the entire rail system works from a programming perspective).

I decided that during my visit to Prague I'd take the opportunity to extract as much information about the rail system as I could from the rest of the team. That led to HanziQ and I working together for a few days, to the point where we could finally address the problems.

In 0.14 and previous, this is how it would work:

Build any rail -> tell all trains to re-path if their path is invalid
Destroy any rail -> tell all trains to re-path if their path is invalid
Build a train stop -> ...
Destroy a train stop -> ...
Do anything related to trains/rails -> tell all trains to re-path if their path is invalid

Obviously that wasn't so great for performance. Instead of starting by fixing the obvious problem, I took advantage of the fact that building any track would cause every train to re-path.  With a simple way to benchmark the performance, I set to improving the logic trains use to determine if they need to find a new path, and to optimize the path finding code. After that was done, it was just a matter of building a set of conditions of which trains need to be notified when anything rail related is changed.

Some were simple: when a train stop is destroyed, only the trains currently driving to that stop are affected. Others were a little more complicated: when a rail is destroyed, unless it had a signal, train stop, or connected to two or more rails on opposite sides, nobody needs to know it was destroyed. The end result after all this is now building and managing anything in the rail system for 0.15 has almost no measurable impact on performance.

As always, let us know if you have any thoughts or feedback on our [Forum](https://forums.factorio.com/43551).
