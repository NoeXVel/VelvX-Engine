#include "Core/Log.hpp"
#include <chrono>
#include <ctime>
#include <fstream>
#include <iostream>
#include <utility>

namespace VelvX::Log {

static std::ofstream g_file;
static VelvX::Log::ConsoleSink g_sink;

void setLogFile(const std::filesystem::path& path) {
    if (g_file.is_open()) {
        g_file.close();
    }
    if (!path.empty()) {
        g_file.open(path, std::ios::out | std::ios::app);
    }
}

void setConsoleSink(ConsoleSink sink) {
    g_sink = std::move(sink);
}

static void write(const char* level, const std::string& message) {
    const auto now = std::chrono::system_clock::now();
    const std::time_t t = std::chrono::system_clock::to_time_t(now);
    std::tm tm{};
#ifdef _WIN32
    localtime_s(&tm, &t);
#else
    localtime_r(&t, &tm);
#endif
    char stamp[16];
    std::strftime(stamp, sizeof(stamp), "%H:%M:%S", &tm);

    const std::string line = std::string("[") + stamp + "][" + level + "] " + message;
    std::cout << line << std::endl;
    if (g_file.is_open()) {
        g_file << line << '\n';
        g_file.flush();
    }
    if (g_sink) {
        g_sink(level, message);
    }
}

void info(const std::string& message) { write("INFO", message); }
void warn(const std::string& message) { write("WARN", message); }
void error(const std::string& message) { write("ERROR", message); }

} // namespace VelvX::Log