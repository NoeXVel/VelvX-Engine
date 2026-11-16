#include "Core/Trigger.hpp"
#include "Core/Input.hpp"
#include "Core/Log.hpp"
#include "Scripting/ScriptEngine.hpp"

namespace VelvX {

std::vector<Trigger::Binding> Trigger::bindings_;

void Trigger::bindKey(int key, const std::string& bookName) {
    bindings_.push_back({key, bookName});
    Log::info("Trigger: tecla " + std::to_string(key) + " -> book " + bookName);
}

void Trigger::clear() {
    bindings_.clear();
}

void Trigger::fire(const std::string& bookName) {
    ScriptEngine::callBook(bookName);
}

void Trigger::update() {
    for (const Binding& b : bindings_) {
        if (Input::isKeyPressed(b.key)) {
            fire(b.bookName);
        }
    }
}

} // namespace VelvX