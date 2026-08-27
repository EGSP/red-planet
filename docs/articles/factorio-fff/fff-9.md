# Friday Facts #9

- Источник: https://factorio.com/blog/post/fff-9
- Авторы: Tomas
- Дата: 2013-11-22
- Темы: сборка, инструменты, редактор
- Применимость к проекту: низкая
- Что извлечь: Рост времени компиляции C++ как измеряемая величина: анализ включаемых файлов и удаление лишних. Отдельно — устройство редактора карт на слоях и инструментах.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello,

since we started with the Friday Facts series, it seems to me that the Fridays have been coming more and more often. So here we are again with the handful of fresh news from the Factorio back stage.

There is a good news and an even better news regarding the 0.8 release. The good news is that we have a definitive release date now. The date has been set to the Friday, the 6th of December. The even better news is that
  it will be Kovarexs' birthday the day before that, so there will be two reasons to celebrate on that Friday night:).

 On Tuesday the first ever "FactorioCon" took place. It was not really a conference, more like a spontaneous meeting. Here is what happened. In the reaction to the previous Friday post we got a message from one of our players on the forums, that he would be coming to Prague for the week. So we agreed to grab a beer on Tuesday night. In the beginning it was supposed to be just me and Kovarex but in the end all the Factorio team was present and even two more of our fans from Prague came (my brother and a friend of his:)). So we were 7 people. We took a beer and snacks in a pub by the river and had a good chat about the game (and life - the usual topic of pub chats).

Most of the map editor functionality, mentioned in the last post is done. This occupied the majority of my time over the past week. Now it is actually a pleasure to work with . Maybe it is a subjective feeling, because I had to do some maps with the old editor:) Anyway the mechanism of layers and tools is in place and easy to extend in the future. So we will release the new editor into the wild, gather the feedback from people on the forum and then incorporate it back in if necessary.

Includes. They are killing us. Factorio is written in C++ which has an old-school-textually-include-stuff approach to referencing objects in other files. This means that the compile time goes up fast with the growing codebase. Compile time is very unproductive kind of time. And it has been going up steadily. At the moment the Factorio core code base has almost 130k lines of code and it takes a fair amount of time to compile. My 2011 MacBookPro 13" takes already around 8 minutes to build the whole project from scratch. Sometime I take the time to wash the dishes or [do a bit of exercise](http://xkcd.com/303/). But most of the time I just kill those 8 minutes by surfing the web or staring at the rolling compilation log. To battle this we have spent quite some time pruning the includes in the past days (it has not been the first time). Kuba was playing with some existing clang include analyzers while Kovarex wrote his own small tool to gather include statistics and try the trial and error include removal (remove - try compile - iterate). This is what the game development includes (pun intended) as well.

Albert has defeated the deamons of the water and now he started integrating the new terrains together. On the other hand we are still struggling with the algorithm for correcting the terrain border transitions. The fail attempt number 5 (I think) is called "the weeping terrain":

![](https://cdn.factorio.com/assets/img/blog/fff-9-terrain-weep.jpg)

 To keep the tradition rolling, the thread for comments is available [on our forum](https://forums.factorio.com/forum/viewtopic.php?f=38&t=1693).
