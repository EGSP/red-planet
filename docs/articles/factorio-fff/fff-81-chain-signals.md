# Friday Facts #81 - Chain signals

- Источник: https://factorio.com/blog/post/fff-81
- Авторы: kovarex
- Дата: 2015-04-10
- Темы: геймдизайн, симуляция, процесс
- Применимость к проекту: средняя
- Что извлечь: Разбор устройства цепных сигналов на железной дороге: правила занятия участков и предотвращение взаимных блокировок. Плюс статистика трёх лет разработки.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello hello,

### 3 years of Factorio in numbers

The initial Factorio commit was done 31.3. 2012, so Factorio is in the development for 3 years and 11 days.

Our effort in numbers:

- In development for 1106 days.

- 88 public releases.

- 14 082 commits in the master branch.

- 204 917 lines of code, 546 339 words and 7 693 483 characters, this is equivalent to 15 average books.

- 20 791 different sprites with 54 114 147 non empty pixels.

- 1492 resolved bugs (only counting those reported on our forums).

- 3027 lines in the changelog.

Results in:

- 56 500 Youtube videos

- 403 000 Google hits of factorio.

- 873 509 unique visits of www.factorio.com

- 75 146 forum posts

- and finally 74 914 copies sold.

This leads me to different kind of comparisons: There are 2.7 lines of code per one buyer, 12.7 commits per day or 2.7 Youtube videos per one sprite, I could go like this for a while :)

### The chain signal in detail

We already told that our plan for 0.12 contains the chain signals, but it's the time now to explain the details. The chain signals have some similarities with the TTD pre signals, but as they actually work differently, we also decided to call them differently to avoid confusion.

In TTD, the signals play very different role in the train pathfinding logic, as they often tell trains where to go.
In Factorio, trains know exactly where to go, and signals are mainly just preventing them from crashing to each other or blocking each other. The only role the signals have in the path finding is, that whenever would the train have to stop because of the red signal, it tries to recalculate its path.

**The definition:**
*While normal signal prevents train from entering the occupied block, chain signal prevents train from entering the block also when the exit isn't free. When more exits exist, the one relevant to the train path is taken into account.*

We don't have the graphics for chain signal ready yet, so I will use a signs to differentiate chain and regular signals.

**Regular signal:**

This is how the regular signal works, the block it guards is empty, so the train can go there:

![](https://cdn.factorio.com/assets/img/blog/fff-81-regular-signals.jpg)

**Simple example:**

Chain signal with one exit doesn't allow the train to enter the block, as the exit is occupied.

![](https://cdn.factorio.com/assets/img/blog/fff-81-chain-signal-basic.jpg)

**Simple example with practical usage:**

The chain signal prevents the train from blocking the other route while waiting.

![](https://cdn.factorio.com/assets/img/blog/fff-81-chain-signal-guards-crossroad.jpg)

**Double crossing:**

Double crossings are the cause of train jams often, as trains tend to get blocked in the middle of the crossing. It can even cause total deadlock, which require manual intervention to fix the problem. With chain signals, the non-blocking rails are still separated, but trains won't stay in the middle of the crossing.

![](https://cdn.factorio.com/assets/img/blog/fff-81-double-crossroad.jpg)

**Deadlock prevention:**

One of the typical situation with train deadlock problem is the usage of single train line for both direction with occasional bypasses. The example of the blocked situation with regular signals:

![](https://cdn.factorio.com/assets/img/blog/fff-81-train-deadlock.jpg)

With chain signals, this problem can be totally avoided by preventing the train from going to the shared section unless it can exit it.

![](https://cdn.factorio.com/assets/img/blog/fff-81-chain-signal-prevents-deadlock.jpg)

As you can see, the chain signals add a lot of possibilities to the train system coordination, mainly for larger and more crowded systems. The implementation of the chain signal is almost complete, just some bugs and automated tests of it have to be finished, so this will get to 0.12 for sure.

The thread for your comments and opinions is ready [at our forums](https://forums.factorio.com/forum/viewtopic.php?t=9586).
