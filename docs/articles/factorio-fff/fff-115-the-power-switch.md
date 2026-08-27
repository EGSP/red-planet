# Friday Facts #115 - The power switch

- Источник: https://factorio.com/blog/post/fff-115
- Авторы: kovarex
- Дата: 2015-12-08
- Темы: производительность, симуляция, графы
- Применимость к проекту: высокая
- Что извлечь: Электрическая сеть как граф: рубильник соединяет и разделяет сети, поэтому требуется эффективный пересчёт связных компонент при изменении графа.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello!

### The third stable candidate

The 0.12.20 was [just released](https://forums.factorio.com/forum/viewtopic.php?t=18182), and it is the third stable candidate in a row, let's keep fingers crossed.

### Power switch (technical)

The graphics of the power switch [was presented already](https://www.factorio.com//blog/post/fff-102), but the integration of it had to wait until this week. Here is the result:

![](https://cdn.factorio.com/assets/img/blog/fff-115-power-switch.gif)

Note, that the power switch can be controlled manually when it is not connected to circuit network, which is what I'm doing here.

Looks simple right? Let me explain some of the Factorio internals behind it.

First I need to explain the basics of the electric network. On the next picture, there are two discrete electric networks (*Network 1* and *Network 2*) that are just about to be connected by building the pole in the middle:

![](https://cdn.factorio.com/assets/img/blog/fff-115-connecting-networks.jpg)

Once the pole is built, the smaller network (*Network 2* in this case) will be merged into *Network 1*. This means that all the poles and entities connected to them need to be iterated and their network ownership updated.

![](https://cdn.factorio.com/assets/img/blog/fff-115-connecting-networks2.jpg)

Similar logic has to be done when the pole is removed, so the network is split instead.

The problem is that electric networks in Factorio can get quite huge so connecting two huge networks can be a performance burden. When building poles manually, it can be neglected, as it happens quite rarely, but with the new switch functionality, networks connected by the switch can connect/disconnect quite often.

We chose to solve it by using the idea of having Network and sub-network concepts:

The electric sub-network is [connected component](https://en.wikipedia.org/wiki/Connected_component_(graph_theory)) of a [graph](https://en.wikipedia.org/wiki/Graph_theory#Graph).
Which means, that all of the poles in the network are directly or indirectly connected.

Then, we construct a higher level graph, where these components are just a single nodes, and power switches that are turned on are verticies between these nodes. One electric network (higher level network) is now one connected component of this higher level graph:

![](https://cdn.factorio.com/assets/img/blog/fff-115-sub-networks1.png)

The great thing is, that turning the switch on and merging the networks now doesn't require to update all of the (possibly thousands) of entities in the *Network 2*, only the *sub-network 3* as a whole is reconnected:

![](https://cdn.factorio.com/assets/img/blog/fff-115-sub-networks2.png)

### Trailer + Trailer2

As part of preparations for our steam wedding day, we will not only update our [current trailer](https://www.youtube.com/watch?v=9yDZM0diiYc) (it won't change so much), but we will also create a second trailer. It will try to provide more info about the game to someone who saw the first trailer but wants to know more. It will be also 2 minutes long and it will have a narration explaining what is Factorio about with the help of ingame graphics.

Albert and Robert are working on this and as Albert wanted to have the voice acting done properly this is what he constructed in the office:

![](https://cdn.factorio.com/assets/img/blog/fff-115-studio-box.jpg)

The trailers probably won't be updated until January, but from what I saw, it looks really nice.

### Compilation times (C++ and technical)

Compilation times in C++ suck. The fact that I have to wait 3 and half minute whenever Factorio is recompiled makes me impatient and ineffective. Apart the [well known reasons]( http://stackoverflow.com/questions/318398/why-does-c-compilation-take-so-long/318440#318440), there is the problem that std (standard template library) and boost headers don't care much about include optimisation, so including single file from these can lead up to hundreds of thousands of lines in the module. This slows down the compilation even when it is preprocessed in precompiled header.

This gave me an idea of just writing our own subset of std+boost (the parts we use the most). The string class for example would be simple class instead of huge one based on generic templates that can't be even fast forwarded. This could also allow us to do some tight Factorio specific optimisations as a bonus. Before even considering doing something like this, I would like to know if there isn't actually someone who made this kind of project, if you know about something like that, let me know.

P.S. I know about the modules proposal, but it will take years until it gets to standard and implemented. Unity build is another thing on my list to try out.

As always, let us know what you think on [our forums](https://forums.factorio.com/forum/viewtopic.php?t=18183).
