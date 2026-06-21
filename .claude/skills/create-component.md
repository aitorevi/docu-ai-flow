# Skill: create-component

Patrón para crear componentes y páginas en Astro 6 con TypeScript estricto.

## Diferencia entre página y componente

- **Página** → va en `src/pages/`, define una ruta, puede hacer fetch de datos
- **Componente** → va en `src/components/`, recibe props, no tiene ruta propia

## Patrón de página

```astro
---
// src/pages/items.astro
import type { Item } from '../types/item';
import ItemCard from '../components/ItemCard.astro';

const res = await fetch(`${import.meta.env.API_URL}/api/items`);
if (!res.ok) throw new Error(`Failed to fetch items: ${res.status}`);
const items: Item[] = await res.json();
---

<main>
  <h1>Items</h1>
  {items.map(item => <ItemCard item={item} />)}
</main>
```

## Patrón de componente

```astro
---
// src/components/ItemCard.astro
import type { Item } from '../types/item';

interface Props {
  item: Item;
}

const { item } = Astro.props;
---

<article>
  <h2>{item.name}</h2>
</article>
```

## Tipos compartidos

Define los tipos en `src/types/` para reutilizarlos entre páginas y componentes:

```typescript
// src/types/item.ts
export interface Item {
  id: number;
  name: string;
}
```

## Reglas

- Fetch siempre en el frontmatter (bloque `---`) — nunca en `<script>` cliente
- Props siempre tipadas con `interface Props`
- Sin `any` — si el tipo es desconocido, define la interfaz mínima necesaria
- Nombres de archivos en PascalCase para componentes: `ItemCard.astro`
- Nombres de archivos en kebab-case para páginas: `items.astro`

## Manejo de errores en fetch

```astro
---
const res = await fetch(`${import.meta.env.API_URL}/api/items`);
if (!res.ok) throw new Error(`API error: ${res.status}`);
const items: Item[] = await res.json();
---
```

Astro captura el error y muestra una página de error en desarrollo.
En producción, configura un `src/pages/404.astro` y `src/pages/500.astro`.

## Commits durante la implementación

```
feat(frontend): add items page fetching from API
feat(frontend): add ItemCard component
feat(frontend): add Item type definition
```
