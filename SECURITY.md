# Segurança

Não abra uma issue pública com webhook, token, senha, endereço interno, dados de clientes ou logs contendo informações pessoais.

Para reportar uma vulnerabilidade, abra um contato privado com os mantenedores do repositório. Até que exista um canal privado definido, remova os segredos do arquivo e revogue imediatamente as credenciais expostas.

## Recomendações

- Use variáveis de ambiente ou um cofre de segredos para webhooks.
- Crie um webhook exclusivo por ambiente e faça rotação periódica.
- Execute o serviço com a menor permissão necessária.
- Restrinja a saída de rede ao domínio do gateway utilizado.
- Revise logs antes de enviá-los para terceiros; eles podem conter nomes de usuário, hostnames e IPs.
- Não trate este agente como substituto de uma solução de monitoramento com alta disponibilidade.
