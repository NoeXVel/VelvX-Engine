#pragma once
/**
 * @file ThreadPool.hpp
 * @brief Pool de threads com submit retornando std::future.
 */

#include <condition_variable>
#include <functional>
#include <future>
#include <memory>
#include <mutex>
#include <queue>
#include <thread>
#include <vector>

namespace VelvX {

/**
 * @brief Pool fixo de workers para tarefas assíncronas (cenas, imports).
 */
class ThreadPool {
public:
    /** @brief Cria o pool com @p threads workers (0 = hardware_concurrency). */
    explicit ThreadPool(size_t threads = 0) {
        if (threads == 0) {
            threads = std::max(1u, std::thread::hardware_concurrency());
        }
        for (size_t i = 0; i < threads; ++i) {
            workers_.emplace_back([this] { loop(); });
        }
    }

    /** @brief Encerra o pool após drenar a fila. */
    ~ThreadPool() {
        {
            std::lock_guard<std::mutex> lock(mutex_);
            stop_ = true;
        }
        cv_.notify_all();
        for (auto& w : workers_) w.join();
    }

    ThreadPool(const ThreadPool&) = delete;
    ThreadPool& operator=(const ThreadPool&) = delete;

    /** @brief Enfileira uma tarefa e retorna o future do resultado. */
    template <class F>
    auto submit(F&& f) -> std::future<std::invoke_result_t<F>> {
        using R = std::invoke_result_t<F>;
        auto task = std::make_shared<std::packaged_task<R()>>(std::forward<F>(f));
        std::future<R> fut = task->get_future();
        {
            std::lock_guard<std::mutex> lock(mutex_);
            queue_.emplace([task]() { (*task)(); });
        }
        cv_.notify_one();
        return fut;
    }

private:
    void loop() {
        for (;;) {
            std::function<void()> job;
            {
                std::unique_lock<std::mutex> lock(mutex_);
                cv_.wait(lock, [this] { return stop_ || !queue_.empty(); });
                if (stop_ && queue_.empty()) return;
                job = std::move(queue_.front());
                queue_.pop();
            }
            job();
        }
    }

    std::vector<std::thread> workers_; ///< Workers do pool.
    std::queue<std::function<void()>> queue_; ///< Fila de tarefas.
    std::mutex mutex_; ///< Protege fila/stop.
    std::condition_variable cv_; ///< Sinalização de tarefas.
    bool stop_ = false; ///< Flag de encerramento.
};

} // namespace VelvX