# ProcessSupervisor

ProcessSupervisor monitora i programmi definiti in `apps_config.json`. Le richieste esterne possono avviare soltanto un programma il cui `id` è già presente in quel file: il JSON della richiesta non può specificare percorsi o argomenti di esecuzione.

## Richieste di avvio esterne

La cartella di ingresso e quella di archivio si configurano nella schermata **Modifica Configurazione**. Per impostazione predefinita sono `REQUESTS` e `REQUESTS\ANALYZED`, accanto all'eseguibile. ProcessSupervisor controlla la cartella di ingresso ogni secondo e, dopo aver letto il file, lo sposta nell'archivio prima di elaborare l'avvio. I file elaborati, le richieste rifiutate e gli avvii sono registrati nel log giornaliero in `LOG`.

Creare un file `.json` nella cartella di ingresso con l'ID configurato:

```json
{
  "id": "morti_di_sonno"
}
```

Se il programma è già attivo, la richiesta viene registrata come ignorata. Per chiedere esplicitamente una seconda istanza, impostare `forceNewInstance` a `true`:

```json
{
  "id": "morti_di_sonno",
  "forceNewInstance": true
}
```

Le istanze avviate in questo modo compaiono separatamente nella griglia e sono monitorate e gestibili come gli altri processi. Per evitare di leggere un file mentre è ancora in scrittura, il chiamante può scriverlo con estensione temporanea e rinominarlo in `.json` una volta completato.
