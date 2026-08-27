# Friday Facts #271 - Fluid optimisations & GUI Style inspector

- Источник: https://factorio.com/blog/post/fff-271
- Авторы: kovarex
- Дата: 2018-11-30
- Темы: производительность, симуляция, инструменты
- Применимость к проекту: высокая
- Что извлечь: Оптимизация расчёта жидкостей и инспектор стилей интерфейса, показывающий устройство окна прямо в игре.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## Game Developers Session 2018

[GDS 2018](http://www.gdsession.com/) will be taking place next week, running from Friday 7th to Saturday 8th. This year, like last year, we are silver sponsors of the event, which means you will see some Factorio branding around the event and in their official booklet. Part of the preparation on our side was to produce a nice graphical asset for their use, which you can see below:

![](https://cdn.factorio.com/assets/img/blog/fff-271-factorio-cover-gds-corrected-2.png)

The image is an aesthetic composition to showcase the design and theme of the game and its elements (while not necessarily making logical sense), and also contains the first public display of our new official Wube Software logo.

About half the office team here will be attending the event, so if you are also going you might bump into us.

## Fluid optimisations

We are tackling the fluid changes in 3 stages:

Move all the fluid logic into a separate system.
Merge straight sections of pipes into segments.
Tweak the fluid flow logic, which will not be an optimisation, but a gameplay mechanics improvement ([FFF-260](https://www.factorio.com/blog/post/fff-260)).

Dominik has just finished stage 1, and it has been merged into 0.17, so let me present what was changed, and how it helps. The approach is similar to what we did with transport belts ([FFF-176](https://www.factorio.com/blog/post/fff-176)).

One of the main things slowing down the simulation is that every entity handling fluid (pipe, assembling machine with fluid connection, refinery, mining drill with fluid connection, pump, offshore pump) has to be kept as updatable and its update needs to be called every tick just to update the flowing of the internal pipes. So mining drills/assembling machines are being forced to do its logic instead of going to sleep.

From the perspective of optimisation, every extra piece of data that needs to get to the CPU cache is like an extra weight you need to carry around. It slows the whole simulation down.

The pipe is used just to call the inner update:

![](https://cdn.factorio.com/assets/img/blog/fff-271-0.16-pipe-update.png)

The other problem is, that the pipe memory is scattered around randomly. So we cut all the fluidboxes from the entities using them, and put them in a separate system (we call it the Fluid Manager). It is storing the fluidboxes like this:

![](https://cdn.factorio.com/assets/img/blog/fff-271-0.17-fluidboxes.png)

The advantage of having data in continuous memory is mainly the fact that when CPU is copying the data from memory to cache, it is doing it in chunks, so when updating one thing, the next one is already in the cache. Modern CPUs are also smart, and they can detect continuous memory access, so they start prefetching the subsequent fluidboxes automatically in advance.

But it is not that simple, as the fluidbox always has neighbours (as the flow is from one fluidbox to others), and one of the neighbours can be on the other part of the continuous memory, out of cache, so it is still not perfect.

The next step was to divide the fluid boxes in something we call a *Fluid system*. A fluid system is basically all the Fluidboxes that are connected together. So, for example, in this refinery setup there are 5 fluid systems.

![](https://cdn.factorio.com/assets/img/blog/fff-271-5-fluid-systems.png)

Each of the fluid system is now its own separate continuous memory of fluid boxes in it. The first advantage of this is that it increases the chance that the neighbours of a water pipe are close (memory wise) to the original pipe, the cached data from touching it for update could still be there when it is being touched as a neighbour.

But the second advantage is even greater, as the fluid systems are now **independent** and fluid flow doesn't interfere with anything else in the map, their update can be completely parallelized without any worries. So we just did that.

The final benchmark on a real fully producing map that uses pipes for production of materials and power (nuclear reactors), I was able to see a 6.5 times speedup of only the pipes update. The speedup wasn't so big on less beefy computers as it depends on the CPU, cache sizes, CPU core count etc. but was still around 3 times faster. I also expect the speedup to get bigger with bigger factories with more separate fluid systems and also with future CPU with more cores.

Dominik did benchmarked every step with a real save game on his *reasonable* i7-4790k, and recorded with the following overall performance gains.

![](https://cdn.factorio.com/assets/img/blog/fff-271-benchmark.png)

(The graph shows some unexplained intermediary steps.)

Just a reminder that this is just a stage 1, before actually merging straight pipe sections into one fluid box which should improve things again. Even in systems with a lot of branching, as even 2 fluidboxes merged into 1 should help. In the example of refinery setup above, it seems that the fluidbox count could be reduced by factor of 4 or so. That should make the savings big enough to justify the planned 2 times slowdown of the future fluid flowing algorithm, as we will probably need 2 passes to make it work good enough. Dominik will have an update on the algorithm and conclusions from the previous discussions in a future FFF.

## GUI style inspector

The implementation of several GUI redesigns is in progress, and it is being done by several team members. Suddenly we realized that coordinating more people brings new problems (what a surprise^^). We started to have mess in the styles, as everyone was inventing their own styles for the GUI to be same as the graphical mock-ups given by the graphics department.

After some discussions of how to solve this, we realized, that we mainly need some common language with the graphical department when it comes to the style hierarchy names. For that we need everyone to be able to quickly inspect which styles are used where and what is the hierarchy. For that reason, we made the GUI style inspector. By pressing a key (Control + F6 by default), every UI element will show a tooltip with information about the widget and its style, and will highlight of the widget as well. We wanted to use it only internally first, but we decided, that if it shows some info for the GUI created by scripts/mods, like name and type, it might be also useful for mod writers to be able to quickly inspect what is going on.

![](https://cdn.factorio.com/assets/img/blog/fff-271-style-inspector.png)

We even use colors to help us navigate:

Red: The value was needlessly redefined (which was happening a lot).
Green: The style value that is being used
White: A style value that is not used as it is overwritten by a descendant style.

This tool (even when quite easy to add), immediately became very useful and it has already helped us to clean up some mess, and should improve the work efficiency when it comes to further GUI implementation. The main reaction when I showed this to the rest of the team was, "Why didn't we have this earlier?"... Well, better late than never.

 This also means, that we are showing quite a lot about how the GUI style is organized to the players, so we need to be extra careful about making it tidy, to avoid bug reports about 'the weird mess' in the styles.

As always, let us know what you think on our [forum](https://forums.factorio.com/63736).
