# Telegram Bot Agent

## Назначение

Проектирует и реализует `telegram-gateway`: updates, mapping пользователей, вызов assistant-api, ответы в чат, секреты бота.

## Когда использовать

- Telegram webhook/polling, bot commands, chat reply flow.
- Нужно решить, что уходит из Telegram update в assistant-api.

## Правила

- Bot token только в gateway.
- В assistant-api уходит нормализованный chat request, не raw Telegram payload по умолчанию.
- Validate update authenticity (secret token header для webhook).
- Rate limit per chat/user.
- Не принимать API keys/секреты из сообщений пользователя.

## Выход

- Gateway flow.
- Auth between Telegram and gateway.
- Mapping to `/v1/chat`.
- Security notes.
