# Friday Facts #55 - MP preview

- Источник: https://factorio.com/blog/post/fff-55
- Авторы: Tomas
- Дата: 2014-10-10
- Темы: детерминизм, тестирование, сеть
- Применимость к проекту: высокая
- Что извлечь: Проверка детерминизма: состояния клиентов сравниваются по контрольным суммам, расхождение локализуется до конкретной подсистемы. Приём применим к воспроизведению записанных партий.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Good evening everyone,

we have spent the week doing various stuff. Playing and testing the multiplayer game, working on some promised features (yes, talking about the tank) and also sorting out administrative issues. In the end we have signed an agreement to rent a place in the Prague city center so since this week we officially have an office space. The place is still empty but we are already looking for furniture and computer equipment to turn an ordinary looking flat it into a true Factorio HQ.

### First MP Footage

Based on a popular demand we have made a short preview of how the current multiplayer looks like. Have a look at the video below where me and kovarex are building an iron ore expansion together and then attacking some biters nests. We were sitting in the same room and the computers communicated via our LAN. Kovarex is running Windows while I am on the Mac OSX. The sound quality is terrible, we are aware of (and sorry for) that. But it should suffice as a proof of concept. Actually this was the second attempt to make the video. The first one ended really fast with repetitive train-related desync. So we stopped to track it down (read below how we do that) before redoing the video again. The result is about 13 minutes of various gameplay (building, exploring the map, riding a vehicle, fighting) without a desync.

### Determinism testing

In the past week, we have often spent the time in the evenings playing our ongoing multiplayer game (that is the one at the video). It is a refreshing fun and currently our main tool to find determinism issues. We really want to get as much determinism issues as possible sorted out before releasing the first multiplayer version. Normally we play with the Heuristic CRC check switched on. This is an incomplete but very fast check taken every tick. It captures only couple of crucial information about the game state like for example state of all the players, number of active entities or snapshot of production statistics. These were selected to be fast to compute yet to be related to a significant portion of the full game state. The CRC checks are compared for all the players in the game for every tick (with a communication latency) and if they don't match the desync happens and desynced players are automatically reconnected and they redownload the map.

When this happens we usually stop to inspect the difference between generated saves at the time of the desync. We have a special branch which inserts human readable tags into the save (which are normally just binary data) that allows us to get a better idea at what part of the code the desync happened. Below is the screenshot from the diff for the bug with the trains mentioned earlier.

![](https://cdn.factorio.com/assets/img/blog/fff-55-diff.jpg)

Sometimes checking such an output is enough to figure out what is wrong (and once we know that fixing the issue is usually the easier part). However often the save difference is one big mess or it is not obvious how the difference happened. Then we need to get some heavier debugging tools. This means makig a full game state CRC check every tick. That is rather slow (depends on the map size) but very accurate way of seeing the difference between game states for different peers. Another tool we have is automatically saving - loading - checking the game every tick. This is also very slow but useful to find problems in setup mechanisms which are often quite complicated.

With the above methodology we have managed to solve all the determinism bugs we found so far. The last serious one was related to blocked items on the transport belts (more precisely on the transition from splitter to the transport belt). It was resisting for a long time (matter of days) because we could reproduce it only in one specific and rather big save which made the debugging really slow. In the end after quite some detective work we found that the problem was simply missing functionality (items blocking mechanism) in the splitter and in the transport belt to ground. Below you can see how the blocked situation looked in one peer and how it looked in another one (difference in the shape of green lines => different state of the game => desync).

![](https://cdn.factorio.com/assets/img/blog/fff-55-belts.jpg)

### Get ready for Spitters

The biters have been really lonely in the game for a long time. That will soon change. Albert has been working for a while on the new addition to the enemy race - The Spitters. They are more evolved sibling of the biters who attack their enemies (that is you) by spitting aggressive acid substance from distance. The graphics are almost finished and we should be able to squeeze the full integration into the 0.11. So if all goes well it will be the tank (Kuba is working on this one at the moment) vs. the Spitters in the 0.11.

Let us know what you think about the multiplayer video [at our forums](https://forums.factorio.com/forum/viewtopic.php?f=38&t=6093).
