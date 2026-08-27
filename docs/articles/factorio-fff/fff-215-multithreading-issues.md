# Friday Facts #215 - Multithreading issues

- Источник: https://factorio.com/blog/post/fff-215
- Авторы: kovarex, Klonan
- Дата: 2017-11-03
- Темы: многопоточность, отладка
- Применимость к проекту: высокая
- Что извлечь: Разбор ошибки многопоточности: редко воспроизводимое состояние гонки и способ его локализации.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello, it is Friday once again.

### Multiple starting areas (Klonan)

To make PvP fun, and fair, it is pretty important that all the teams have a fair start. We do this for the normal game by using some special logic to generate a specific starting area. While the map generation didn't support this, I worked around the issue by copy-pasting all the starting area chunks to new starting areas for all players. This looked very odd though, and since it was done in the Lua script, was very slow.

![](https://cdn.factorio.com/assets/img/blog/fff-215-copy-areas.png)

So the solution is to fix the map generator to create multiple starting points, which with all the refactoring work by TOGoS, was not so hard to do. Whereas before all the logic calculated its own 'distance from the start' from (0,0), now it looks for the closest starting area, and calculates the distance from that. This actually fixes and simplifies a lot of the PvP script, as I also don't have to worry about clearing away huge biters nests from each teams front door.

![](https://cdn.factorio.com/assets/img/blog/fff-215-new-starting-areas.png)

This is a much cleaner solution, as the new starting areas are seamlessly integrated into the map.

### Multithreading issues (kovarex)

We were always saying, that we keep the full update multithreading as the last ace in the sleeve to be used after the normal optimisations become too difficult. Apart from the plan to split update into chunks and update them separately, there are also smaller and simpler things we can do.

We have for example this part of the update step, where we update trains, electric network and belts, and we update it one after another like this:

![](https://cdn.factorio.com/assets/img/blog/fff-215-update-serial.png)

But these tasks are almost independent as neither trains or belts use electricity and belts don't interact with trains. There would be some details that would have to be solved, like Lua API calls and inserter activation order, but other than that it would be pretty straightforward. So I tried to run these things in parallel and measured how fast it would be:

![](https://cdn.factorio.com/assets/img/blog/fff-215-update-parallel.png)

To my surprise, the parallel version didn't speed things up, it was actually even slower. First I thought that it is caused by the fact, that the threads steal each others cache or the task is too short to be parallelized etc., but then I realized, that we already do the "prepare logic" in parallel. The prepare logic gathers all the data (sprite draw orders) for rendering and it is doing in parallel up to 8 threads. The task is iterating through the game world, and it is also quite short. Yet still the parallelization works pretty well there. After a few days (and many cppcon videos about multithreading later), I got the answer to my question. The answer is the same as many other answers related to performance, it is caused by cache invalidation.

There is some shared cache for all the threads (L3 I believe), and some smaller caches specific for each of the cores (L2 and L1 I believe). The cache is divided into some kind of pages. The problem is that as long as I don't control the memory allocation, the data of 3 objects that might be completely independent can end up in the same page of memory cache. So lets imagine this situation:

![](https://cdn.factorio.com/assets/img/blog/fff-215-one-piece-of-memory-cache.png)

Even if two different cores work with the same part of memory, they don't slow each other down as long as they only read the memory (Which is the case of our prepare logic as it is read only operation) as each core has **its own copy of the memory page**. The problem arises when the operation is not read only:

![](https://cdn.factorio.com/assets/img/blog/fff-215-cache-page-copies.png)

Each thread has its own copy of the page, but whenever something is changed, the other copies of the same page need to be invalidated and updated. This means that the threads are invalidating each others cache all the time, which slows the whole process so much that it is slower than the non-parallel solution.

How do we solve it? The solution would probably be to organize the memory allocation in such a way, that everything that is on the same chunk, or related to some specific task would have to use its own memory allocator that keeps things in one place together in the memory. Every chunk would have its own piece of memory dedicated to entities in the chunk, trains would have all its data in one piece of memory together etc. This way, it shouldn't happen that two tasks would work on one piece of memory at the same time so the multithreading would actually speed things up.

The problem with this is, that it requires big changes in the code as not only the entities itself but all their dynamically allocated data would have to be always allocated depending on the entity position. Whenever the entity moves between chunks, all its data structures would have to be re-allocated in the new chunk. All pointers to any data structures in entity would have to be properly updated as they move around etc. This means, that once (if) we decide to do this step, it would probably be one big update related only to this and this is not something we have time to do before 1.0.

As always, let us know what you think on our [forums](https://forums.factorio.com/53769).
