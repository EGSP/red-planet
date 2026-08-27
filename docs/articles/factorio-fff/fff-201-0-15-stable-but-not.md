# Friday Facts #201 - 0.15 Stable, but not really

- Источник: https://factorio.com/blog/post/fff-201
- Авторы: kovarex, Rseding
- Дата: 2017-07-28
- Темы: производительность, сериализация, загрузка
- Применимость к проекту: высокая
- Что извлечь: Сокращение времени загрузки сохранения и запуска игры: разбор того, на что уходит время при чтении карты, подготовке атласов и обработке модов.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello,
the 0.15 has been declared stable. Unfortunately we found some smaller problems, so there is going to be at least one bugfix release. One of the problems we discovered yesterday, is a glitch in the blueprint transferring logic that results in the transfers stopping forever when a player that is just transferring his blueprint into the game leaves. I'm quite surprised that I found it out myself when I was testing something else, and we didn't have a single bug report regarding it.

## Blueprint library versus mod issues

This is a great example to show why complexity of software grows faster than linearly with the amount of features. We have mods and we have the blueprint library. Both systems work when used separately, but new problems come when you use these two features together. The [bug report](https://forums.factorio.com/51333) is basically describing that when you have blueprints with modded content, and you switch to vanilla (or different mod setup), all the mod related content in the blueprint library is removed upon opening any game. This makes these blueprints useless if you want go back, enable the mods and use the blueprints again.

This is can be very annoying for the player, and after quick a discussion we decided to solve it by having blueprint library separated for every mod configuration and by allowing players to transfer library contents from different mod configurations to the current one on demand. We decided to not push this into 0.15 as we are too afraid to break things now, so it is going to be 0.16 feature.

## Blueprint interaction while being downloaded

The second major problem with the blueprint library is the UX, especially in multiplayer. When the blueprint/book, is big and is being transferred while the player is already holding it, it can't be moved to inventory/quickbar, and the 'drop here to export to main inventory' button does nothing but print a message to the console.

The plan is to make a special item type, that will be basically a promise of blueprint and use it in this case. Once the blueprint transfer into the game is finished, the item will change into the blueprint. When the player related to it will leave, or remove the blueprint from his library, the item will just disappear. It can even have nice info into tooltip about what is going on. This is (obviously) also going to be in 0.16 not 0.15.

Generally speaking, the blueprint library is a new thing to Factorio, so there several other UX tweaks and polishes we want to do.

## Rail block visualisation finished

The rail segment visualisation that I described in [FFF 198](https://www.factorio.com/blog/post/fff-198) is now completed.
The main problem I had to solve is how to color the segments to make sure, that two neighbour segments don't have the same color. Some of you probably know, that this is well known thing called [graph coloring](https://en.wikipedia.org/wiki/Graph_coloring). For plain graphs (which the rail segments are), it has been proven long time ago, that 4 colors is enough to color every graph, but the proof and algorithm is not simple. But for 6 colors, the algorithm is pretty easy, the only downside is, that it is designed to color the graph as a whole, which is fine when the game is being loaded, but not acceptable when adding a single signal to a huge rail network, I needed something incremental.

In the meantime I wanted to test the visualisation graphics proposal from Vaclav, so I made a very simple logic that picks the lowest available color index that is not already taken by neighbour rails to see how it looks. I was surprised to find out that in normal rail system, it doesn't use more than 3 colors most of the time, and it is very rare to see the 5th color . The conclusion is simple, this is good enough. Although it is possible to construct rail system that results in our algorithm putting two of the same colors next to each other, it is not really a problem, as it doesn't break anything, and in a normal game, the chance of it happening is close to 0. The takeaway is that it is important to realize how strict you need to depending on the situation.

Here is an example of intersection with a missing signal, how fast can you find it the old way?

![](https://cdn.factorio.com/assets/img/blog/fff-201-intersection-without-colors.jpg)

How fast can you find it when you see the visualisation?

![](https://cdn.factorio.com/assets/img/blog/fff-201-intersection-with-colors.jpg)

I guess that this topic is closed, the fact that 2-3 colors are used most of the time makes the visualisation not look too colorful and it will help both new players to understand the concept of blocks as well as help experienced players when signalling an intersection to see what is going on at a glance.

## Optimisations Rseding91

There's always something else that can be optimized. Last week we purchased some new hardware because I wanted to test if 'throwing money at it' was a viable method of improving compilation times (and the speed at which we can develop/fix things). We purchased a brand new i9-7900X CPU and compatible hardware with great results. After some difficulties getting everything setup it was almost 150% faster than the previous setup I was using. After that significant improvement in compilation times that got me wondering what else I do multiple times per day development wise that I could speed up. That led to game startup time, map loading/saving, and map generation.
I grabbed a relatively large save file with a large factory setup (Colonelwill's current map) and set out to get a baseline. The save file is 45.7 MB compressed and 132 MB uncompressed. In 0.15.31 it takes 3.89 seconds to load and 2.46 seconds to save.

I think I can safely say that nobody likes waiting for the game to save (myself included); it's distracting and breaks you out of whatever you're currently doing in the game. While working on potential improvements to save time I measured exactly what happens when I tell the game to save and the results gave me a little more appreciation for just how fast saving actually is.

### Technical/numbers

The way saving works in Factorio is through a process called serialization. We go over the entire loaded map beginning to end and write out each piece that's needed to restore the map. Every time something needs to be saved it calls the seralizer method 'write' with the data and how many bytes it should serialize. On the 132 MB of data that makes up the uncompressed map the 'write' method on the seralizer is called 84'272'542 times. Of those 84 million calls to write 99% are < 10 bytes.

| Data size | Write count |
|---|---|
| 0 bytes | 51'527 |
| 1 bytes | 47'267'254 |
| 2 bytes | 24'264'184 |
| 3 bytes | 124 |
| 4 bytes | 11'277'751 |
| 5 bytes | 791 |
| 6 bytes | 971 |
| 7 bytes | 591 |
| 8 bytes | 1'390'402 |
| 9 bytes | 870 |
| 10+ bytes | 18'077 |

The largest was only 121 bytes (called 84 times). In total 152'472'991 bytes were processed making for an average of 1.8 bytes per call. The entire process completed in 2.46 seconds for an average of 334'257 calls per millisecond. The serializer we use for saving the map uses several memory buffers that it cycles internally as it writes one buffer to disk in parallel with the rest of the map saving logic so there wasn't much performance to gain in this logic. But, seeing the numbers gave me a better picture of just how much work happens when the map is saved. In the end I found a few small things to improve on  such that saving the map in 0.16 now only takes 2.25 seconds (a 9% improvement). Not what I hoped for but an improvement is an improvement.

### Map load time

When looking at the time spent loading maps I found that we had a similar memory buffer system to saving but with 1 major difference: when the buffer ran out it would stop and refill the buffer from disk. That was an obvious slow spot with an easy fix: use multiple buffers and load them from disk in parallel with the map deserialization. I also found some inefficiencies in how trains were loaded causing them to run O(Rails * Trains) operations every time the map was loaded which I fixed to just O(Trains). The end result being what took 3.89 seconds to load in 0.15 now takes 2.8 seconds to load in 0.16 (a 28% improvement).

### Game startup time - Graphics

On this new computer with normal graphics quality Factorio takes 9.84 seconds to reach the main menu. I think that's pretty good for a game these days but as we work on Factorio we close and restart the game 100s of times per day. Every time we change something, test a save file from a bug report, or as I was doing: testing out changes to see how they impact performance I have to wait for the game to start and it's wasted time. There's a disabled config option that takes all of the loaded graphics the game uses and packs them into 1 large file that can be read at startup. It mostly helps when you frequently restart the game - which is exactly what we do every day. With that enabled the game launches in 5.31 seconds (a 46% improvement).

### Game startup time - Mods

We don't tend to use mods while we're working on new features so some performance problems related to them go unnoticed. One bug report recently had 111 mods the person was using which exposed a slowdown in processing all of the mods at startup. When mods are loaded we track what a mod changes, to record the history of what mods change what game objects. Originally we did this using the C++ Boost library "property tree" class, but it turned out to be faster to write our own logic using Lua than use the Boost class. Since that change we wrote our own version of the property tree class and in testing it showed to be even faster than doing the logic inside Lua. In 0.15.31 it took 28.72 seconds to process the mods and now in 0.16 it only takes 20.2 seconds (a 29% improvement). Not only does this benefit the players but it helps us because we don't have to spend as much time waiting for the game to load when working on modded bug reports.

### Game startup time - Sounds

With the loading of graphics and mod data no longer being the slowest parts the next thing that started taking the majority time was loading sounds. I realized, after my work on improving map loading, that there wasn't any reason I couldn't just load sounds in parallel with the graphics. It was a 20 minute 'fix' and now by the time graphics are finished loading, sounds have long finished meaning it can proceed straight to the main menu.

### Game startup time - Conclusion

With graphics being loaded from the atlas-cache, mod data processing being almost twice as fast, and sounds loaded in parallel I can now launch the game and be at the main menu ready to play in just 2.962 seconds (a 44% improvement). The change was interesting to get used to over the past week but now when I need to test things in 0.15 I find myself annoyed at how long everything takes compared to the 'new hotness' of 0.16.

### Map generation - New maps

Unless I'm working on some specific save file I generate maps almost as frequently as I restart the game while working on bug fixes/new features. I've never considered generating a new map particularly slow but it never felt fast either. 0.15 already generates the map in the background while the game runs to prevent stuttering while new chunks are generated but the starting area when making a new map is not. I changed it so the starting area would generate in multiple threads if possible (while still maintaining determinism). The end result being: 0.15.31 generates a new map in 1.83 seconds and 0.16 generates a new map in 0.8 seconds (a 56% improvement).

### Conclusion

Overall the start-game/load/generate game experience is measurably faster. Most importantly it *feels* noticeably faster, and makes for a better overall experience when we don't have to wait for things. With the belt optimizations and everything mentioned here, 0.16 is already promising to improve performance in multiple areas and full 0.16 development hasn't even begun, with other ideas we've talked about in previous Friday Facts still to be implemented promising even better performance.

As always, leave us any feedback or comments over on our [forum](https://forums.factorio.com/51347)
