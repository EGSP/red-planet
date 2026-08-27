# Friday Facts #274 - New fluid system 2

- Источник: https://factorio.com/blog/post/fff-274
- Авторы: Dominik, Klonan, kovarex
- Дата: 2018-12-21
- Темы: симуляция, производительность
- Применимость к проекту: высокая
- Что извлечь: Итоговый алгоритм расчёта жидкостей: слияние соседних объёмов в общий сегмент, за счёт чего стоимость обновления перестаёт зависеть от длины трубы.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## New Fluid system 2 (Dominik)

Hi Factorians,

Here is Dominik, with an update on the fluids. This time it is pretty much finished so I can tell you facts instead of just speculations. You will find how the new algorithm will work and some new handy usability features.

In [FFF-260](https://factorio.com/blog/post/fff-260) I wrote about how it all started, why we are doing it and what the plan is. There was a huge response from you all and I want to thank everyone for their contributions. Let me apologise to redditors, as at the beginning I started responding on the forums and when I realized there is reddit too, there were too many comments for me to handle.

The forum users produced many ideas on how the system could work. About third of them was a fluid teleportation, many where known but many were entirely new and interesting. What intrigued me was the large variety of backgrounds they came from - differents kinds of engineers (mechanical, CS, electrical, ...), mathematicians, physicists, and even people with real pipes hands on experience. I won’t go through them here, you can find them on the forums or reddit. There were two proposals on the forum though that were so good that they made it into the game - from quinor and TheYeast.

Both of these proposals were very similar and kinda similar to the previous game logic. What it shares is that the mechanic still uses fluid physics simulation and volume in a pipe as a base for the movement calculation. As a result, not much changes on the first glance. What they add though is an emphasis on the fluid network update being independent on the current state (i.e. updating one pipe only depends on state from the last tick) and is therefore independent on evaluation order, which was one of the big pains of the old model that led to sometimes ridiculous junction behavior. Difference between these two was rather small - quinor’s version allowed perfect throughput with 3 passes over the fluidboxes (fluidbox is the thing managing fluids for entities, so I will talk about them), while Yeast’s one was 2 pass with ¼ throughput. What was outstanding though is that TheYeast, a physicist, supported the model with a nice theoretical background and what’s more, he made an amazing [JS simulator](https://jsfiddle.net/TheYeast/c9d2azjo/show) to test and compare various modification of the model. Because that extra pass in quinor’s version was too high a price for the perfect throughput, I went with TheYeast’s two pass one.

Since the old algorithm only used a single pass run by entities for the update, I first needed to overhaul the whole system to allow accommodating the new one. Going from one pass to two passes necessarily means higher complexity, so we made a big effort to optimize everything we could to make sure we will still end up faster than 0.16. Kovarex wrote about it in [FFF-271](https://factorio.com/blog/post/fff-271).

### The new algorithm

The new algorithm follows realistic wave equations. It works with two variables.

The volume of a fluid in a fluidbox (FB) and the corresponding column height.
The flow speed in a connection between fluidboxes.

The exact behavior then depends on two constants:

C^2 - corresponds to the mass of the liquid. Affects how quickly changes (waves) propagate.
Friction - affects how quickly throughput drops with pipe length.

The first good news is that these variables can now be set for fluids separately so different behaviour can be achieved. E.g. crude oil pipeline will require some pumps while steam will be totally fine.

![](https://cdn.factorio.com/assets/img/blog/fff-274-fluid-friction.gif)

The two pass algorithm for one update (leaving out many details) is then as follows:

Update flow speeds on all connections

A. d = difference of fluid column (input has always 0 and output has max)

B. d *= c^2

C. (mechanism for damping waves)

D. Flow speed += d

E. Clamp the flow to take max ¼ of contents (otherwise we could get below 0 - remember that we only use last tick information); fluid can go over max temporarily.
Move the fluids along all connections

A. Just move 'speed' amount of fluid from one FB to another

This algorithm is so simple and works so well, and also requires only very small changes, that it was a clear winner. The main downside is that it can only move ¼ of FB content in one tick, so FBs are enlarged to compensate. Another is that the fluids may seem to travel quite slowly into an empty pipe, but that is actually quite realistic and looks nice. The third issue might be some waves and oscillations, which are a result of the realistic model, and are very small with the current dampening model. This could be limited even further by introducing continuous fluid production/consumption, but it does not seem necessary at the moment.

What you get over 0.16 is that the fluids now behave correctly and intuitively, performance is consistent (pipes to ground won’t help you with throughput anymore), different fluids actually move differently.

![](https://cdn.factorio.com/assets/img/blog/fff-274-fluid-splitting.gif)

As a small but handy improvement, you can now see flow rate information in the entity info of each pipe.

![](https://cdn.factorio.com/assets/img/blog/fff-274-fluid-info.png)

My big thanks go to quinor and mainly TheYeast for coming up with the model and doing a lot of work with me on tuning it and finding improvements to make the behaviour as nice as it is now. In case you are interested in more detail, see TheYeast’s [forum posts](https://forums.factorio.com/search.php?author_id=35849&sr=posts) and [simulator source code](https://jsfiddle.net/TheYeast/c9d2azjo).

### Efficiency

The overhauls and optimisations in [FFF-271](https://factorio.com/blog/post/fff-271) cut the update time by some 50% and up to 10x on some high-end CPUs.

Introducing the new algorithm made it immediately 30% slower. Long story short, through various fixes, including a small change that made the algorithm 1 pass again, this increase was cut down to 15%. So the overall result is that the fluid update is still a lot faster than it was before. I am still debating the segment merging, as it is not that simple and it would cost the simulation some detail. It is a low priority at this point compared to other parts of the update time.

### Fluid Mixing

![](https://cdn.factorio.com/assets/img/blog/fff-274-gone.png)

That’s right. No more fluid mixing.

Once an empty fluid system (connected network of fluidboxes - pipes, crafting machines etc.) touches either a fluid or a fluid filter, the system is locked onto that fluid. It is then not possible to connect it to another system with a different fluid. There are quite a few actions which can result in merging systems, so each needed to be checked:

Building an entity with fluidboxes (Eg. pipes, pumps, storage tanks)
Setting a recipe with a fluid input/output
Rotating an entity

![](https://cdn.factorio.com/assets/img/blog/fff-274-no-mixing.gif)

 In order to use the system for a different fluid, the system must first be reset - by draining its fluid and removing all the filters.

Please note that old saves have to work with this so if your save currently contains some fluid mixing setup, it will be automatically fixed upon loading it. This will be done during migration either by removing some fluids, unsetting recipes, or destroying entities if need be.

## macOS developer needed (Klonan)

At the start of this week our long time macOS maintainer and web admin HanziQ let us know he was leaving the team and moving on to other projects. He has been part of the Factorio team for nearly 4 years, and in that time has contributed a lot to the game and community. We all wish him the best in his future endeavours.

HanziQ leaving, along with the departure of our other macOS developer Jiří, means we currently have nobody on the team to work with and maintain the macOS version of the game. This is a pretty significant issue, as we have the largely untested GFX engine rewrite due to be released with 0.17. If you know anybody who may be able to help us fill this position, please direct them to our [macOS developer job listing](https://factorio.com/job/macos-game-developer).

## Steam keys direct from us (Klonan)

We have long sold the game through our own website, which involves receiving a code and then registering an account etc. I have received quite some community feedback, and from this feedback we have decided to start selling Steam keys directly through our site. On the buy page you will be given the choice of a Website key or Steam key.

[![](https://cdn.factorio.com/assets/img/blog/fff-274-buy-steam-key.png)](https://factorio.com/buy)

We hope this will be more convenient for a lot of people, especially for those wanting to gift a copy of the game this holiday season.

## Steam awards 2018 (Klonan)

The [Steam awards 2018](https://store.steampowered.com/SteamAwards/2018/) voting has begun, and Factorio is nominated for 'Most fun with a machine'. There are also 2 other Czech games nominated for the same category, so the country is quite well represented.

## Animal named after the game (kovarex)

A new species of scorpion *Neobuthus Factorio* was just identified and classified. My father has a hobby of going (not only) to dangerous places and identifying undiscovered species of scorpions and spiders. He offered to name one of his classifications after the game for fun. You can find the full publication [here](http://www.kovarex.com/scorpio/pdf/2018o-Neobuthus_271.pdf).

As always, let us know what you think on our [forum](https://forums.factorio.com/64009).
