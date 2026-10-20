<p align="center">
  <img src="images/banner.png" alt="Moonward" width="720" />
</p>

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

Sobre el Starward original, las acciones habituales caben en un acceso directo de escritorio y una sola URL, con mejoras en el gacha, los registros de combate, la instalación de juegos, los fondos y más. Funciones principales:

#### Gacha

- **Historial de gacha** — Las estadísticas de banners se reordenan arrastrando y quedan fijas arriba. Se puede alternar entre vista de lista y cuadrícula compacta de iconos. Las rachas de UP / fallos y la tasa de acierto del 50/50 se ven de un vistazo
- **Filtro y compartir** — Un desplegable en la barra de título elige qué banners mostrar, con seleccionar todo / invertir / restablecer. Un clic genera una imagen para compartir con estilo esmerilado, con el conteo de pity y el progreso de la garantía
- **Sincronización de gacha** — Genshin Impact (servidor de China) y Zenless Zone Zero pueden sincronizar registros desde miHoYo BBS; Zenless Zone Zero también admite HoYoLAB. Los objetos nuevos que aún no están en el catálogo reciben icono y nombre automáticamente. Los nombres de los objetos siguen el idioma de la aplicación
- **Miliastra Wonderland** — La página del historial de deseos funciona como la del historial de gacha (filtro, vista compacta, reordenar arrastrando, imagen para compartir) y completa los iconos de objetos que faltan en el original
- **UIGF** — Importa / exporta registros de gacha en formato UIGF

#### Registros de combate y caja de herramientas

- **Check-in diario** — Check-in en miHoYo BBS / HoYoLAB, interruptores por juego, check-in automático y de recuperación. Mientras reside en la bandeja, vuelve a hacer check-in tras cada reinicio diario
- **Inicio de sesión** — Servidores de China con código SMS; servidores internacionales con inicio de sesión web. Las sesiones caducadas se renuevan automáticamente cuando es posible, para no tener que iniciar sesión una y otra vez
- **Actualización automática de registros** — Define una frecuencia por cuenta y por sección de datos (incluidos el mes actual y el anterior de los informes mensuales); se actualizan automáticamente al iniciar la aplicación
- **Informes mensuales y notas** — Los informes mensuales de la caja de herramientas (Calendario mensual del trazacaminos / Informe mensual de Inter-Knot / Diario del viajero) comparten diseño. El informe de Inter-Knot cuenta los datos diarios en la zona horaria del servidor y muestra el mes actual por defecto. Las notas en tiempo real de Genshin Impact muestran más detalles. Hay un acceso de verificación cuando salta el control de riesgo
- **Presentación de registros** — Los registros de combate y los informes mensuales se pueden exportar como imagen para compartir. El Teatro Imaginarium marca los personajes de prueba y de apoyo; Anomaly Arbitration muestra las victorias rápidas y los nombres de dificultad
- **Juego en la nube** — Consulta el tiempo restante de Cloud Genshin y Cloud Zenless Zone Zero y reclama automáticamente el tiempo gratuito diario

#### Inicio

- **Varios perfiles de inicio** — Conjuntos ilimitados de argumentos de inicio y lanzadores personalizados por juego. Cada perfil tiene su propio argumento DX12 y la opción «iniciar mediante CMD»; ponle nombre a un perfil y crea un acceso directo en el escritorio
- **Herramientas de terceros** — Un lanzador personalizado también puede ser un enlace de protocolo URL. Los argumentos de línea de comandos habituales incluyen grupos para BetterGI, ZZZ OneDragon y March7th Assistant
- **Protocolo URL** — `moonward://` inicia / detiene / reinicia un juego, perfil y cuenta concretos, o solo hace el check-in. Se puede incrustar en scripts o páginas web (consulta [docs/UrlProtocol](UrlProtocol.md))
- **Inicio rápido** — El menú hamburguesa de la página de inicio reúne la configuración del juego, el inicio rápido y los accesos directos del juego. Los accesos directos pueden iniciarse sin el aviso de UAC, y las tareas programadas correspondientes se gestionan por juego en la configuración
- **Tiempo de juego** — Mientras el juego está abierto, el botón Iniciar juego muestra un temporizador. Las estadísticas de tiempo de juego permiten cambiar de juego, elegir año y mes y generar una imagen para compartir. El tiempo que el PC pasa en suspensión no se cuenta

#### Instalación y actualizaciones

- **Servidores con enlaces físicos** — Al actualizar o reparar un juego también se procesan los demás servidores que comparten archivos mediante enlaces físicos (la instalación base siempre se actualiza primero). El diálogo de desinstalación muestra todos los servidores instalados del mismo juego y puede quitarlos a la vez
- **Opciones de instalación** — Zenless Zone Zero puede instalar los recursos completos o los básicos. Junto a la ruta de instalación se indica el tipo de disco (SSD / HDD / extraíble / red). Las descargas por chunks se hacen en paralelo por bloque
- **Reparación e información de versión** — La reparación tiene ahora su propio diálogo y puede reparar solo el Miliastra Sandbox; el número de hilos de verificación se ajusta al tipo de disco. La página «Información de versión» de la configuración del juego muestra la versión, los parches incrementales y el MD5 del ejecutable principal. La búsqueda automática encuentra los juegos instalados desde la aplicación (incluidos los enlaces físicos)

#### Apariencia y fondo

- **Fondos Trust** — En Zenless Zone Zero, los fondos animados Trust y los fondos estáticos Mindscape de la wiki se pueden descargar y usar como fondo personalizado. El modo aleatorio cambia la imagen al iniciar, al cambiar de juego o al abrir desde la bandeja. La galería usa la caché local; las actualizaciones se comprueban en silencio en segundo plano
- **Fondo personalizado** — Diálogo propio para imagen / vídeo (arrastra a la página de inicio para reemplazar). La elección del póster se mantiene tras actualizarse la lista de fondos
- **Fondos de vídeo** — Los vídeos que no se pueden decodificar por hardware se convierten a H.264 automáticamente. Si falta la extensión HEVC / VP9, la aplicación guía su instalación o muestra una imagen fija. Con la pantalla apagada se libera el decodificador, así que ya no consume CPU
- **Barra de herramientas de inicio** — Cronología (cuentas atrás de banners / eventos), códigos de canje de las retransmisiones (servidor de China) y mapas interactivos (Genshin Impact / Honkai: Star Rail)

#### Otros

- **Residente en la bandeja** — Inicio opcional en la bandeja al iniciar sesión. Los atajos globales y el control con mando siguen funcionando; el menú de la bandeja puede activarlos y buscar actualizaciones
- **Sincronización LAN** — Trae registros de gacha, datos de la caja de herramientas y tiempo de juego desde otro dispositivo de la red local. Solo se añaden los registros que faltan en este PC; las credenciales de inicio de sesión nunca se sincronizan
- **Gestión de datos** — La base de datos, la caché, los registros y los fondos se guardan en una carpeta de tu elección. Desde la configuración se puede hacer copia de seguridad de la base de datos y vaciar la caché. En el primer inicio puedes importar datos del Starward original (solo lectura; los datos originales no se tocan)
- **Comentarios** — La página Acerca de rellena la información del entorno con un clic, abre la página de issues de GitHub y abre la carpeta de registros
- **Actualizaciones silenciosas** — Las nuevas versiones se descargan en segundo plano (también se comprueban periódicamente desde la bandeja) y se instalan al salir o en el siguiente inicio, sin interrumpir un juego cuyo tiempo se está registrando. Tras instalar se muestra el registro de cambios. Los paquetes se descargan primero de CNB y, si falla, de GitHub (Velopack)

Los instaladores están en [GitHub Releases](https://github.com/TurmoilZoom/Moonward/releases); desde China continental también se pueden descargar en [CNB](https://cnb.cool/TurmoilZoom/Moonward/-/releases).

Proyecto original: [Scighost/Starward](https://github.com/Scighost/Starward)  
Agradecimientos: [CREDITS.md](../CREDITS.md) (proyectos de código abierto de referencia en funciones y diseño)  
Licencia: [MIT](../LICENSE)

Privacidad: [docs/Privacy.md](Privacy.md)
