# Red → Green → Refactor

TDD es un ciclo de tres fases con tres mentalidades distintas. Confundirlas es el error más común.

---

## Red — hablas con el problema

El test falla porque el comportamiento **no existe todavía**. Eso es correcto.

Reglas en esta fase:
- Escribe el test antes que el código
- El test debe fallar **por la razón correcta** — si falla por error de compilación o por un bug en el propio test, no es Red válido
- El nombre del test debe entenderlo alguien de negocio, no solo un desarrollador
- No escribas nada de implementación todavía

```
test: add failing test for <behavior>
```

---

## Green — hablas con el compilador

El objetivo es **el código más simple posible** para que el test pase. Nada más.

Reglas en esta fase:
- La fealdad es temporal y aceptable
- No anticipes casos futuros
- No refactorices todavía — primero haz que pase
- Si necesitas hardcodear un valor para pasar, hazlo

```
feat: implement <behavior> to pass test
```

---

## Refactor — hablas con el siguiente desarrollador

Los tests son tu red de seguridad. Si un test se rompe, deshaz el cambio.

Reglas en esta fase:
- El test siempre en verde durante el refactor
- Mejora claridad, elimina duplicación, nombra mejor
- Si un cambio rompe un test, no lo fuerces — reconsidera el diseño
- Solo commitea si hay cambios reales

```
refactor: clean up <area>
```

---

## El ciclo completo

```
Red → Green → Refactor → Red → Green → Refactor → ...
```

Cada iteración añade un comportamiento. Al final tienes código que funciona, tests que documentan el comportamiento, y un diseño emergente limpio.
