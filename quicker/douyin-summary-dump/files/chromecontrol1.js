async function readSummaries() {
  const raw = localStorage.getItem('dy-ad-skip:summaries:v1');
  return raw === null ? '[]' : raw;
}
readSummaries();
