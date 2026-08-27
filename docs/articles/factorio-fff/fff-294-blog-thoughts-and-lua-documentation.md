# Friday Facts #294 - Blog thoughts & Lua documentation improvements

- Источник: https://factorio.com/blog/post/fff-294
- Авторы: Klonan, Bilka
- Дата: 2019-05-10
- Темы: процесс, документация
- Применимость к проекту: низкая
- Что извлечь: Статистика просмотров блога и роль еженедельной записи в работе команды; улучшение документации Lua API.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## Blog thoughts Klonan

As the time goes on, the nature of our weekly FFF post has changed. At the very beginning ([FFF-1](https://www.factorio.com/blog/post/fff-1)) it was to let people know that "we're still alive and working on the game", and over time we've grown into covering a range of different topics:

Communicating our progress and roadmap of the next releases.
Showing new features and gathering community feedback on them.
Diving into the technical side of game development and particular challenges we face.
'Meta-posts' about the company and the changes outside of the game.
Community spotlights and interesting Factorio related news.

It is always an interesting challenge each week to determine what topics we might be able to cover in the FFF. During the weeks of rapid development the FFF can feel like a triumphant reveal of what we have been working on, and we excitedly await the community response. Other times, such as when most of the team is on bugfixing, we can take the oppourtunity to explore other points of discussion, such as the marketing post last week.

The graph of the FFF readership over the last year is quite informative to look into:

![](https://cdn.factorio.com/assets/img/blog/fff-294-blog-graph.png)

We had a good run back in January and February, we had week after week of really interesting posts and a build-up of excitement for the 0.17 release. Now after the release, the readership has stabilized at around 40-45,000 views a week (note, that the graph does not include people reading the blog post through Steam).

The FFF is close to its 300th post now, with no signs of stopping soon, and the continued audience of dedicated readers each week help to keep us on track and focused on our quest towards 1.0. As we get closer to completion of the game, the general nature of the blog post will no doubt change even further. The good times of showing a new feature each week might be over, but I hope we will be able to provide interesting insights into the game and our development processes. I would also like to thank all the players/readers who share their thoughts with us each week, it is really great to have so much support and care for our project.

## Lua API documentation improvements Bilka

The [Lua API documentation](https://lua-api.factorio.com/) is one of the most valuable resources for modders: It is generated directly from the game's source code, so it completely covers all scripting functionalities. Furthermore, additional pages cover some general concepts such as in what order mod files are loaded or how to store persistent data. However, a big issue with these pages was that they were linked at the very bottom of the main page, below two long lists of classes and events. This means that they were very easy to miss.

To remedy this, all additional pages are now linked at the top of the main page and accompanied by a short description of the structure of the API. To further support getting a quick overview, I added a page about general Lua libraries that Factorio changes. These changes could be frustrating traps where it would take users a very long time to find out that some functions, such as getting the current real time, were not available. The new main page organization should serve well to highlight this new page to make the discovery of these changes much easier.

As always, let us know what you think on our [forum](https://forums.factorio.com/70645).
