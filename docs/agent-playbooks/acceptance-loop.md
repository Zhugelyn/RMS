# Acceptance Loop Playbook

## Loop

1. Прочитай `memory/current-project.md`, `memory/phase-plan.md`, `memory/run-log.md`.
2. Возьми один незакрытый slice / checkbox текущей фазы.
3. Сформулируй machine-checkable criteria.
4. Реализуй минимум.
5. Запусти проверки.
6. Если fail — почини и повтори. Не останавливайся на первой ошибке, если чинится локально.
7. Security pass, если затронуты API, secrets, Telegram, tokens.
8. Обнови repo memory + короткий Automation Memory.
9. Стоп, если criteria green или нужен человек.

## Stop conditions

- Все criteria текущего slice зелёные.
- Нужен секрет, доступ, решение человека.
- Следующий шаг выходит за текущую фазу.

## Output

```markdown
## Slice
- id:
- done:
- checks:
- leftover:
- memory updates:
```
