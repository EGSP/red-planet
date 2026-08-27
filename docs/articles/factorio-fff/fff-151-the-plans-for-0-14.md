# Friday Facts #151 - The plans for 0.14

- Источник: https://factorio.com/blog/post/fff-151
- Авторы: kovarex
- Дата: 2016-08-12
- Темы: многопоточность, производительность, сеть
- Применимость к проекту: высокая
- Что извлечь: Разделение цикла обновления на потоки: какие подсистемы допускают параллельный расчёт, а какие обязаны остаться последовательными ради детерминизма.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello!

### 0.14 plans

Most of the list of 0.14 plans won't be a big surprise as most of it was discussed in the past:

- Multiplayer rewrite

- Circuit network improvements

- Update optimizations

- Nuclear power

- Liquid wagon + universal barrelling

- High res graphics introduction

- Gui reskin and possible improvements

- Multithreaded update

We expect this update to take less time compared to 0.13, so November might be a realistic estimate.

### Multiplayer rewrite

I dedicated [fff-147](https://www.factorio.com/blog/post/fff-147) and [fff-149](https://www.factorio.com/blog/post/fff-149) to these. All of the big changes are finished and all of the previously written automated tests are passing.
The rewrite has been merged into our master branch so it can be tested by team members from now on. All the remaining changes and tweaks to be done should be possible without any additional deep changes, so it should go smoothly from now on.
This means, that I will have time to get to work on the next 0.14 subject soon, which is:

### Circuit networks and 0.14

Over time Twinsen made quite a list of features or improvements worth considering to be added to the circuit network. It's time to look at that list and see what should be done for 0.14

Right now, the trello card looks something like this:

![](https://cdn.factorio.com/assets/img/blog/fff-151-combinator-trello-card.png)

So what better way to decide than ask the community. There's a poll [at the forums](https://forums.factorio.com/forum/viewtopic.php?t=30853) where you can vote for your favorite features. Of course, keep in mind that these are not fleshed out ideas, some of them might turn out to be completely wrong and not worth implementing.

### The nuclear power

The relatively short-term (steam) vs long-term (solar) power generation options work quite well, but we are well aware that more possibilities to generate power should have existed for a long time already.
The main reason why we postponed it so many times, is that we wanted to do it properly. Some kind of "mine uranium -> put it into boiler" wouldn't be a proper use of the potential at all.
I can't say anything more specific on the subject, as we still have to invent the nuclear power plant mechanics, and design them to be interesting while properly fitting the existing Factorio gameplay. I welcome any good ideas on the topic.

### High res graphics introduction

We have been asked about this many times already since the first example from [quite a long time ago](https://www.factorio.com/blog/post/fff-64). We didn't forget it, and as we want this to be done prior the 1.0 version, it is the latest possible time to start working on it. All of the newly created entities are already created with high res variant in mind, but some of the older ones, like pipes or belts need to be tweaked and post processed again to be compatible with the rest.
This is what Vaclav is working on these days. We should hopefully be able to show some examples of the work soon. We will cover just part of the graphics in 0.14, but you will have the chance to use them when you select the new graphics option. But beware, the graphics memory requirements will grow a lot.

### Update optimisations

The belt and pipe optimisation have been described in the [fff-148](https://www.factorio.com/blog/post/fff-148), on top of it, we would like to take a look on some of the biggest cpu eaters in big factories. It depends on the type of the factory, but
train pathfinding/movement, flying robots logic and inserters are the next most probable candidates. Additionally, we would like to try out the multithreaded update, which is covered in the next part.

### Multithreaded update

As it is always better to improve the performance without the need to use more threads, there is always a limit. This is why we would like to try experiment with the multithreaded update.
We already use multithreading in Factorio, but only for certain tasks. To update the game at the same time when it is rendered, to speed up the render prepare (this is how we call gathering of the data for render), and to generate the map in the background.

So it looks like this:

![](https://cdn.factorio.com/assets/img/blog/fff-151-current-threads.png)

There are other threads, like sound or network packet processing, but these are not related to this.

Multithreading the update itself, which is the real bottleneck in big factories is the most needed step currently, but it is also the most complicated. Factorio needs to be fully deterministic
and many things are tightly related across the whole map.
This is why we won't *just make the whole update run in threads* as it would be huge amount of work that would break almost everything. We will just pick some specific processing tasks
suitable for multithreading and implement these,
while the rest of the logic will run in single thread as before:

![](https://cdn.factorio.com/assets/img/blog/fff-151-future-threads.png)

If it works well, we will be able to put more and more things into the multithreaded updates one by one, so we have it under control.

The first testing entity that will be ran in parallel is inserter. The selection is logical, they are tens of thousands of inserters in Factories regularly, they are usually one of the most
cpu hoggers right after belts and they have limited operational range, meaning, one inserter can't directly access something 2 chunks away.

The main problem when dealing with parallelism is access to shared resources, eg. when two different threads write to the same piece of memory. This is either solved by locks, which is not really an option on the
entity level, or by strategy that prevents the threads to write to the same piece of memory. Our planned strategy is to divide the chunks (32X32 tiles of map) into 3X3 grids and number these from 1 to 9 like this:

![](https://cdn.factorio.com/assets/img/blog/fff-151-parallel-update-chunk-distribution.png)

No, it is not a sudoku :), all the chunks with number 1 can be updated at the same time, as long as the logic doesn't change anything further than the neighbour chunks. Then we update chunks 2, 3 up to 9.
We don't really have to wait for all of the 1s to be finished to start 2, but I don't want to run into details.
This strategy has also the additional needed property, which is that the neighbours of the chunks are updated always in the same order. Without it, the game wouldn't be deterministic.

But even inserters affect global state of the map, mainly:

- Circuit network can span across the whole map - when the inserter adds/removes from a chest connected to circuit network it could access the same data as other thread.

- Logistic network

- Production statistics (Burner inserter can use coal)

These changes can't be applied instantly anymore, but every thread keeps its own list of changes to be applied later, we call these *deltas*. These deltas are merged at the end of the multithreaded phase, and applied in a single thread later on.

This is the plan, and our new potential programmer (say hello to Denis) is doing it as his testing task. Our testing task difficulty kind of increased, but he was asking for it :). It could actually be working quite soon, I can't wait for it!

### Overall plan

Our overall plan is to have Factorio 1.0 finished next summer. This most probably means, that 0.15 would become 1.0 once it gets stable. We have not decided what will go into 0.15 yet, but the current plans
contain the ideas of the dirty mining and artillery train, but this is all subject to change. We will mainly try to polish the corners, so the overall result is balanced solid and finished game.

This will be the result of 5 years long hell of a ride, from home garage developement, through crowdfunding to our offices and steam. Finishing the game will be important step for us and our conscience, as all the promises we gave on the way will be delivered.

After that, we will probably take a reasonably long vacation to clear our heads and then ... well, I find it very probable that we will still want to work on the game. The meaningful possibility as I see it would be to make proper expansion. That way we could implement the ideas like the space platform, biter farms and more with energy and time it deserves.

As always, let us know what you think on [our forums](https://forums.factorio.com/30854).
