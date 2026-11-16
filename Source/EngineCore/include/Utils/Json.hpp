#pragma once
/**
 * @file Json.hpp
 * @brief DOM JSON mínimo (parse + serialização) usado por projetos, prefabs e settings.
 */

#include <string>
#include <utility>
#include <vector>

namespace VelvX::Json {

/**
 * @brief Valor JSON: null, bool, number, string, array ou object.
 */
class Value {
public:
    enum class Type { Null, Bool, Number, String, Array, Object };

    Type type = Type::Null; ///< Tipo do valor.
    bool boolean = false; ///< Payload bool.
    double number = 0.0; ///< Payload numérico.
    std::string str; ///< Payload string.
    std::vector<Value> arr; ///< Elementos de array.
    std::vector<std::pair<std::string, Value>> obj; ///< Pares chave/valor (ordem preservada).

    /** @brief Valor null. */
    static Value null() { return {}; }
    /** @brief Valor bool. */
    static Value ofBool(bool b) { Value v; v.type = Type::Bool; v.boolean = b; return v; }
    /** @brief Valor numérico. */
    static Value ofNumber(double n) { Value v; v.type = Type::Number; v.number = n; return v; }
    /** @brief Valor string. */
    static Value ofString(std::string s) { Value v; v.type = Type::String; v.str = std::move(s); return v; }
    /** @brief Array vazio. */
    static Value array() { Value v; v.type = Type::Array; return v; }
    /** @brief Object vazio. */
    static Value object() { Value v; v.type = Type::Object; return v; }

    /** @brief Acessa (criando se preciso) uma chave de object. */
    Value& operator[](const std::string& key);
    /** @brief Acessa uma chave em valor const (null estático se ausente). */
    const Value& operator[](const std::string& key) const;
    /** @brief Busca uma chave sem criar; nullptr se ausente. */
    const Value* find(const std::string& key) const;
    /** @brief Número de elementos (array/object). */
    size_t size() const { return type == Type::Array ? arr.size() : obj.size(); }
    /** @brief Adiciona elemento a um array. */
    void push(Value v) { arr.push_back(std::move(v)); }

    /** @brief Lê como string (default se tipo diferente). */
    std::string asString(const std::string& def = "") const;
    /** @brief Lê como número (default se tipo diferente). */
    double asNumber(double def = 0.0) const;
    /** @brief Lê como bool (default se tipo diferente). */
    bool asBool(bool def = false) const;

    /** @brief Serializa com indentação. */
    std::string dump(int indent = 2) const;
    /** @brief Parse de texto JSON (lança std::runtime_error em erro). */
    static Value parse(const std::string& text);
};

} // namespace VelvX::Json