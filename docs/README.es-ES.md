<h1 align="center">Moonward</h1>

<p align="center">
  Un lanzador de terceros de código abierto basado en <a href="https://github.com/Scighost/Starward">Starward</a> para los juegos de PC de miHoYo<br/>
  <a href="https://github.com/TurmoilZoom/Moonward/releases/latest">Descargar</a>
</p>

<p align="center">
  <a href="../README.md">简体中文</a>
  · <a href="README.zh-TW.md">繁體中文</a>
  · <a href="README.en-US.md">English</a>
  · <a href="README.de-DE.md">Deutsch</a>
  · Español
  · <a href="README.it-IT.md">Italiano</a>
  · <a href="README.ja-JP.md">日本語</a>
  · <a href="README.ko-KR.md">한국어</a>
  · <a href="README.ru-RU.md">Русский</a>
  · <a href="README.th-TH.md">ไทย</a>
  · <a href="README.vi-VN.md">Tiếng Việt</a>
</p>


---

Sobre el Starward original, las acciones habituales caben en un acceso directo de escritorio y una sola URL, con mejoras en el check-in, el gacha y los fondos. Funciones principales:

#### Gacha

- **Historial de gacha** — Las estadísticas de banners se reordenan arrastrando (desplazamiento horizontal automático al acercarse al borde); la lista se desplaza arrastrando; las estadísticas quedan fijas arriba. Rachas UP / fallos y probabilidad de acierto se ven de un vistazo. El pity de Miliastra Wonderland usa una barra de progreso
- **Filtro y compartir** — El desplegable de la barra de título elige qué banners mostrar: seleccionar todo / invertir / restablecer. Un clic genera una imagen mate para compartir, con el pity y el progreso de la garantía
- **Sincronización de gacha** — Genshin Impact / Zenless Zone Zero y otros pueden actualizar el historial por miHoYo BBS. Los personajes nuevos que aún no están en el catálogo reciben icono y nombre automáticamente. Los nombres de objetos siguen el idioma de la app
- **Intercambio de datos** — Importar / exportar historial de gacha en UIGF. Se puede importar el historial de Starward original en solo lectura

#### Cuenta y caja de herramientas

- **Check-in diario** — Check-in de miHoYo BBS / HoYoLAB, interruptor por juego, check-in automático y recuperación. Al iniciar el juego con acceso directo / URL / línea de comandos, esa cuenta también hace un check-in aparte
- **Inicio de sesión** — Servidor chino: código SMS al móvil; servidor internacional: inicio web. Si la sesión caduca, se renueva automáticamente cuando es posible, sin volver a entrar una y otra vez
- **Informes mensuales y notas** — Los informes mensuales de la caja de herramientas (Calendario mensual de exploración / Informe mensual de Inter-Knot / Diario del Viajero) comparten el mismo diseño. El informe de Inter-Knot corrige los datos diarios entre husos horarios y muestra el mes actual por defecto. Si las notas en tiempo real chocan con el control de riesgo, hay una entrada de verificación

#### Inicio

- **Varios perfiles de inicio** — Para un mismo juego se pueden guardar conjuntos ilimitados de argumentos y programas de inicio personalizados. Cambiar o editar no obliga a rellenar de nuevo; se puede nombrar y crear un acceso directo de escritorio
- **Protocolo URL** — `moonward://` inicia / detiene / reinicia el juego, perfil y cuenta indicados, o solo dispara el check-in. Se puede incrustar en scripts o páginas web (véase [docs/UrlProtocol](UrlProtocol.md))
- **Inicio rápido** — El menú hamburguesa de inicio reúne ajustes del juego, inicio rápido y «crear acceso directo del menú Inicio»

#### Apariencia y fondo

- **Fondos Trust** — En Zenless Zone Zero se pueden descargar los fondos dinámicos Trust y los estáticos Mindscape de la wiki y usarlos como fondo personalizado. Al abrir la galería se usa la caché local; las actualizaciones se comprueban en segundo plano
- **Fondo personalizado** — Diálogo propio para imagen / vídeo (arrastrar a la página de inicio para reemplazar). Restaurar desde la bandeja ya no parpadea. Tras actualizar la lista de fondos se conserva la preferencia del póster

#### Otros

- **Integración con el sistema** — Inicio opcional con Windows hacia la bandeja. En Acerca de se rellenan de un clic los datos de diagnóstico, se abre el feedback de GitHub y la carpeta de registros
- **Actualizaciones silenciosas** — La versión nueva se descarga en segundo plano, se instala al salir y el siguiente arranque muestra el contenido de la actualización (Velopack + GitHub Releases)

Los instaladores están en [Releases](https://github.com/TurmoilZoom/Moonward/releases).

Proyecto original: [Scighost/Starward](https://github.com/Scighost/Starward)  
Agradecimientos: [CREDITS.md](../CREDITS.md) (proyectos de código abierto de referencia en funciones y diseño)  
Licencia: [MIT](../LICENSE)

Privacidad: [docs/Privacy.md](Privacy.md)
